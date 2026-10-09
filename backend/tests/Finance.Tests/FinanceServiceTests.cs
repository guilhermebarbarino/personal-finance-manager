using Finance.Application;
using Finance.Domain;

namespace Finance.Tests;

public sealed class FinanceServiceTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 10, 9);

    [Fact]
    public async Task CreateAsync_PersistsAndMapsIncome()
    {
        var repository = new InMemoryRepository();
        var service = new FinanceService(repository);

        var result = await service.CreateAsync(new("Salário", 5000m, ReferenceDate, "INCOME", "Trabalho"), CancellationToken.None);

        Assert.Equal("income", result.Type);
        Assert.Equal(5000m, result.Amount);
        Assert.Single(repository.Items);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownTypeWithoutSaving()
    {
        var repository = new InMemoryRepository();
        var service = new FinanceService(repository);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(new("Teste", 1m, ReferenceDate, "transfer", "Outros"), CancellationToken.None));

        Assert.Empty(repository.Items);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExistingTransaction()
    {
        var item = new Transaction("Compra", 100m, ReferenceDate, TransactionType.Expense, "Mercado");
        var repository = new InMemoryRepository(item);
        var service = new FinanceService(repository);

        var result = await service.UpdateAsync(item.Id, new("Reembolso", 100m, ReferenceDate, "income", "Trabalho"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("income", result.Type);
        Assert.Equal("Reembolso", item.Description);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNullWithoutSaving()
    {
        var repository = new InMemoryRepository();
        var result = await new FinanceService(repository).UpdateAsync(Guid.NewGuid(),
            new("Teste", 5m, ReferenceDate, "expense", "Outros"), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task DeleteAsync_ExistingItem_RemovesAndSaves()
    {
        var item = new Transaction("Compra", 20m, ReferenceDate, TransactionType.Expense, "Casa");
        var repository = new InMemoryRepository(item);

        Assert.True(await new FinanceService(repository).DeleteAsync(item.Id, CancellationToken.None));
        Assert.Empty(repository.Items);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_DoesNotSave()
    {
        var repository = new InMemoryRepository();

        Assert.False(await new FinanceService(repository).DeleteAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task ListAsync_FiltersMonthAndSortsByDateDescendingThenDescription()
    {
        var repository = new InMemoryRepository(
            new Transaction("Zebra", 10m, new DateOnly(2026, 10, 8), TransactionType.Expense, "Outros"),
            new Transaction("Beta", 10m, ReferenceDate, TransactionType.Expense, "Outros"),
            new Transaction("Alfa", 10m, ReferenceDate, TransactionType.Income, "Outros"),
            new Transaction("Outro mês", 10m, new DateOnly(2026, 9, 9), TransactionType.Income, "Outros"));

        var result = await new FinanceService(repository).ListAsync(2026, 10, CancellationToken.None);

        Assert.Equal(new[] {"Alfa", "Beta", "Zebra"}, result.Select(x => x.Description));
    }

    [Theory]
    [InlineData(1999, 1)]
    [InlineData(2101, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void ValidateYearMonth_RejectsOutOfRange(int year, int month)
    {
        Assert.Throws<ArgumentException>(() => FinanceService.ValidateYearMonth(year, month));
    }

    [Fact]
    public async Task DashboardAsync_ComputesTwelveMonthsWithIncomeExpensesAndBalance()
    {
        var repository = new InMemoryRepository(
            new Transaction("Salário", 5000m, new DateOnly(2026, 1, 5), TransactionType.Income, "Trabalho"),
            new Transaction("Mercado", 1200m, new DateOnly(2026, 1, 10), TransactionType.Expense, "Casa"),
            new Transaction("Extra", 500m, new DateOnly(2026, 2, 2), TransactionType.Income, "Trabalho"),
            new Transaction("Outro ano", 900m, new DateOnly(2025, 1, 2), TransactionType.Income, "Trabalho"));

        var result = await new FinanceService(repository).DashboardAsync(2026, CancellationToken.None);

        Assert.Equal(12, result.Months.Count);
        Assert.Equal(5500m, result.Income);
        Assert.Equal(1200m, result.Expenses);
        Assert.Equal(4300m, result.Balance);
        Assert.Equal(3800m, result.Months[0].Balance);
        Assert.Equal(500m, result.Months[1].Balance);
        Assert.Equal(0m, result.Months[2].Balance);
    }

    [Fact]
    public async Task DashboardAsync_RejectsInvalidYear()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new FinanceService(new InMemoryRepository()).DashboardAsync(1999, CancellationToken.None));
    }

    private sealed class InMemoryRepository(params Transaction[] initial) : ITransactionRepository
    {
        public List<Transaction> Items { get; } = [..initial];
        public int SaveCount { get; private set; }

        public Task<List<Transaction>> GetMonthAsync(int year, int month, CancellationToken ct) =>
            Task.FromResult(Items.Where(x => x.Date.Year == year && x.Date.Month == month).ToList());

        public Task<List<Transaction>> GetYearAsync(int year, CancellationToken ct) =>
            Task.FromResult(Items.Where(x => x.Date.Year == year).ToList());

        public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(Items.SingleOrDefault(x => x.Id == id));

        public Task AddAsync(Transaction transaction, CancellationToken ct)
        {
            Items.Add(transaction);
            return Task.CompletedTask;
        }

        public void Remove(Transaction transaction) => Items.Remove(transaction);

        public Task SaveAsync(CancellationToken ct)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
