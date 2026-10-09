namespace Finance.Domain;
public enum TransactionType { Income, Expense }
public sealed class Transaction
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Description { get; private set; } = "";
    public decimal Amount { get; private set; }
    public DateOnly Date { get; private set; }
    public TransactionType Type { get; private set; }
    public string Category { get; private set; } = "";
    public bool? IsPaid { get; private set; }
    private Transaction() { }
    public Transaction(string description, decimal amount, DateOnly date, TransactionType type, string category, bool? isPaid = null)
        => Update(description, amount, date, type, category, isPaid);
    public void Update(string description, decimal amount, DateOnly date, TransactionType type, string category, bool? isPaid = null)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 150)
            throw new ArgumentException("Descrição deve ter entre 1 e 150 caracteres.");
        if (amount <= 0 || amount > 999999999999.99m || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Valor deve ser positivo e ter no máximo duas casas decimais.");
        if (!Enum.IsDefined(type)) throw new ArgumentException("Tipo inválido.");
        if (string.IsNullOrWhiteSpace(category) || category.Trim().Length > 60)
            throw new ArgumentException("Categoria deve ter entre 1 e 60 caracteres.");
        if (type == TransactionType.Income && isPaid is not null)
            throw new ArgumentException("Status de pagamento só se aplica a despesas.");
        Description = description.Trim(); Amount = amount; Date = date; Type = type; Category = category.Trim(); IsPaid = isPaid;
    }
}
