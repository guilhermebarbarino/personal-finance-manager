using Finance.Application;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
namespace Finance.Infrastructure;
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options): DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Transaction>(e => {e.HasKey(x=>x.Id); e.Property(x=>x.Description).HasMaxLength(150).IsRequired(); e.Property(x=>x.Category).HasMaxLength(60).IsRequired(); e.Property(x=>x.Amount).HasPrecision(14,2); e.Property(x=>x.Type).HasConversion<string>().HasMaxLength(20); e.HasIndex(x=>x.Date);});
        model.Entity<AdminAccount>(e => {e.HasKey(x=>x.Id); e.Property(x=>x.Email).HasMaxLength(320).IsRequired(); e.HasIndex(x=>x.Email).IsUnique(); e.Property(x=>x.PasswordHash).IsRequired();});
    }
}
public sealed class AdminAccount {public Guid Id {get; set;} = Guid.NewGuid(); public string Email {get;set;} = ""; public string PasswordHash {get;set;} = "";}
public sealed class EfTransactionRepository(FinanceDbContext db): ITransactionRepository
{
    public Task<List<Transaction>> GetMonthAsync(int year, int month, CancellationToken ct) => db.Transactions.AsNoTracking().Where(x=>x.Date.Year == year && x.Date.Month == month).ToListAsync(ct);
    public Task<List<Transaction>> GetYearAsync(int year, CancellationToken ct) => db.Transactions.AsNoTracking().Where(x=>x.Date.Year == year).ToListAsync(ct);
    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct) => db.Transactions.FirstOrDefaultAsync(x=>x.Id==id,ct);
    public async Task AddAsync(Transaction t, CancellationToken ct) => await db.Transactions.AddAsync(t,ct);
    public void Remove(Transaction t) => db.Transactions.Remove(t);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
