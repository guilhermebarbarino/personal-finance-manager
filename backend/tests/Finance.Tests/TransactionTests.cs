using Finance.Domain;

namespace Finance.Tests;

public sealed class TransactionTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 10, 9);

    [Fact]
    public void Constructor_TrimsDescriptionAndCategory()
    {
        var transaction = new Transaction(" Salário ", 4500.75m, ReferenceDate, TransactionType.Income, " Trabalho ");

        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.Equal("Salário", transaction.Description);
        Assert.Equal("Trabalho", transaction.Category);
        Assert.Equal(4500.75m, transaction.Amount);
        Assert.Equal(TransactionType.Income, transaction.Type);
        Assert.Equal(ReferenceDate, transaction.Date);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(1.001)]
    [InlineData(1000000000000)]
    public void Constructor_RejectsInvalidAmounts(double value)
    {
        Assert.Throws<ArgumentException>(() =>
            new Transaction("Teste", (decimal)value, ReferenceDate, TransactionType.Expense, "Outros"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptyDescription(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            new Transaction(value, 10m, ReferenceDate, TransactionType.Expense, "Outros"));
    }

    [Fact]
    public void Constructor_RejectsTooLongDescription()
    {
        Assert.Throws<ArgumentException>(() =>
            new Transaction(new string('a', 151), 10m, ReferenceDate, TransactionType.Expense, "Outros"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptyCategory(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            new Transaction("Teste", 10m, ReferenceDate, TransactionType.Expense, value));
    }

    [Fact]
    public void Constructor_RejectsTooLongCategory()
    {
        Assert.Throws<ArgumentException>(() =>
            new Transaction("Teste", 10m, ReferenceDate, TransactionType.Expense, new string('a', 61)));
    }

    [Fact]
    public void Constructor_RejectsUnknownTransactionType()
    {
        Assert.Throws<ArgumentException>(() =>
            new Transaction("Teste", 10m, ReferenceDate, (TransactionType)123, "Outros"));
    }

    [Fact]
    public void Update_WithInvalidAmount_DoesNotChangeExistingTransaction()
    {
        var transaction = new Transaction("Antigo", 20m, ReferenceDate, TransactionType.Expense, "Casa");

        Assert.Throws<ArgumentException>(() =>
            transaction.Update("Novo", 0m, ReferenceDate.AddDays(1), TransactionType.Income, "Trabalho"));

        Assert.Equal("Antigo", transaction.Description);
        Assert.Equal(20m, transaction.Amount);
        Assert.Equal(TransactionType.Expense, transaction.Type);
        Assert.Equal("Casa", transaction.Category);
    }

    [Fact]
    public void Update_WithValidData_UpdatesTransactionAndPreservesId()
    {
        var transaction = new Transaction("Anterior", 20m, ReferenceDate, TransactionType.Expense, "Casa");
        var id = transaction.Id;

        transaction.Update(" Atualizada ", 35.25m, ReferenceDate.AddDays(1), TransactionType.Income, " Trabalho ");

        Assert.Equal(id, transaction.Id);
        Assert.Equal("Atualizada", transaction.Description);
        Assert.Equal("Trabalho", transaction.Category);
        Assert.Equal(35.25m, transaction.Amount);
        Assert.Equal(TransactionType.Income, transaction.Type);
    }
}
