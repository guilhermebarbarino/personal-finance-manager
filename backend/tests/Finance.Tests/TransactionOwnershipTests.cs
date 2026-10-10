using Finance.Domain;
namespace Finance.Tests;

public sealed class TransactionOwnershipTests
{
    [Fact]
    public void AssignOwner_RequiresValidId()
    {
        var item = new Transaction("Conta", 10m, new DateOnly(2026,10,10), TransactionType.Expense, "Casa");
        Assert.Throws<ArgumentException>(() => item.AssignOwner(Guid.Empty));
    }

    [Fact]
    public void AssignOwner_IsImmutableAcrossUsers()
    {
        var item = new Transaction("Conta", 10m, new DateOnly(2026,10,10), TransactionType.Expense, "Casa");
        var owner = Guid.NewGuid();
        item.AssignOwner(owner);
        item.AssignOwner(owner);
        Assert.Equal(owner, item.UserId);
        Assert.Throws<InvalidOperationException>(() => item.AssignOwner(Guid.NewGuid()));
    }
}
