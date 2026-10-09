using Finance.Domain;
using Finance.Application;

namespace Finance.Tests;

public sealed class ExpensePaymentTests
{
    private static readonly DateOnly Date = new(2026, 10, 9);

    [Fact]
    public void Expense_CanBeCreatedAsUnpaidAndMarkedPaid()
    {
        var transaction = new Transaction("Boleto", 120.50m, Date, TransactionType.Expense, "Contas", false);
        Assert.False(transaction.IsPaid);
        transaction.Update("Boleto", 120.50m, Date, TransactionType.Expense, "Contas", true);
        Assert.True(transaction.IsPaid);
    }

    [Fact]
    public void ExistingExpense_HasUnknownStatus()
    {
        var transaction = new Transaction("Despesa histórica", 10m, Date, TransactionType.Expense, "Outros");
        Assert.Null(transaction.IsPaid);
    }

    [Fact]
    public void Income_RejectsPaymentStatus()
    {
        Assert.Throws<ArgumentException>(() =>
            new Transaction("Salário", 2000m, Date, TransactionType.Income, "Trabalho", true));
    }

    [Fact]
    public void ExpenseCanBecomeIncome_WhenPaymentStatusIsCleared()
    {
        var transaction = new Transaction("Conta", 90m, Date, TransactionType.Expense, "Outros", false);
        transaction.Update("Reembolso", 90m, Date, TransactionType.Income, "Trabalho");
        Assert.Equal(TransactionType.Income, transaction.Type);
        Assert.Null(transaction.IsPaid);
    }
}
