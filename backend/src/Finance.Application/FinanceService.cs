using Finance.Domain;
namespace Finance.Application;
public record TransactionInput(string Description, decimal Amount, DateOnly Date, string Type, string Category);
public record TransactionOutput(Guid Id, string Description, decimal Amount, DateOnly Date, string Type, string Category);
public record MonthlySummary(int Month, decimal Income, decimal Expenses, decimal Balance);
public record DashboardOutput(int Year, decimal Income, decimal Expenses, decimal Balance, IReadOnlyList<MonthlySummary> Months);
public interface ITransactionRepository
{
    Task<List<Transaction>> GetMonthAsync(int year, int month, CancellationToken ct);
    Task<List<Transaction>> GetYearAsync(int year, CancellationToken ct);
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Transaction transaction, CancellationToken ct);
    void Remove(Transaction transaction);
    Task SaveAsync(CancellationToken ct);
}
public sealed class FinanceService(ITransactionRepository repository)
{
    public async Task<IReadOnlyList<TransactionOutput>> ListAsync(int year, int month, CancellationToken ct)
    {
        ValidateYearMonth(year, month);
        return (await repository.GetMonthAsync(year, month, ct)).OrderByDescending(t => t.Date).ThenBy(t=>t.Description).Select(Map).ToList();
    }
    public async Task<TransactionOutput> CreateAsync(TransactionInput input, CancellationToken ct)
    {
        var t = new Transaction(input.Description, input.Amount, input.Date, ParseType(input.Type), input.Category);
        await repository.AddAsync(t, ct); await repository.SaveAsync(ct); return Map(t);
    }
    public async Task<TransactionOutput?> UpdateAsync(Guid id, TransactionInput input, CancellationToken ct)
    {
        var t = await repository.GetByIdAsync(id, ct); if (t is null) return null;
        t.Update(input.Description, input.Amount, input.Date, ParseType(input.Type), input.Category);
        await repository.SaveAsync(ct); return Map(t);
    }
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var t = await repository.GetByIdAsync(id, ct); if (t is null) return false;
        repository.Remove(t); await repository.SaveAsync(ct); return true;
    }
    public async Task<DashboardOutput> DashboardAsync(int year, CancellationToken ct)
    {
        if (year < 2000 || year > 2100) throw new ArgumentException("Ano inválido.");
        var rows = await repository.GetYearAsync(year, ct);
        var months = Enumerable.Range(1, 12).Select(m => {
            var group = rows.Where(t => t.Date.Month == m);
            var income = group.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
            var expenses = group.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
            return new MonthlySummary(m, income, expenses, income - expenses);
        }).ToList();
        return new DashboardOutput(year, months.Sum(m => m.Income), months.Sum(m => m.Expenses), months.Sum(m => m.Balance), months);
    }
    public static void ValidateYearMonth(int year, int month)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12) throw new ArgumentException("Mês inválido.");
    }
    private static TransactionType ParseType(string type) => type?.ToLowerInvariant() switch {
        "income" => TransactionType.Income, "expense" => TransactionType.Expense, _ => throw new ArgumentException("Tipo deve ser income ou expense.")
    };
    private static TransactionOutput Map(Transaction t) => new(t.Id, t.Description, t.Amount, t.Date, t.Type == TransactionType.Income ? "income" : "expense", t.Category);
}
