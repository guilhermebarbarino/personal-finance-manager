using Finance.Application;
using Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
namespace Finance.Infrastructure;
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options): DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Transaction>(e => {e.HasKey(x=>x.Id); e.Property(x=>x.UserId).IsRequired(); e.HasIndex(x=>new {x.UserId,x.Date}); e.HasOne<AdminAccount>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict); e.Property(x=>x.Description).HasMaxLength(150).IsRequired(); e.Property(x=>x.Category).HasMaxLength(60).IsRequired(); e.Property(x=>x.Amount).HasPrecision(14,2); e.Property(x=>x.IsPaid).HasColumnName("IsPaid"); e.Property(x=>x.Type).HasConversion<string>().HasMaxLength(20); e.HasIndex(x=>x.Date);});
        model.Entity<PasswordResetToken>(e => { e.HasKey(x=>x.Id); e.Property(x=>x.TokenHash).HasMaxLength(64).IsRequired(); e.HasIndex(x=>x.TokenHash).IsUnique(); e.HasIndex(x=>x.ExpiresAt); e.HasOne<AdminAccount>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade); });
        model.Entity<EmailVerificationToken>(e => { e.HasKey(x=>x.Id); e.Property(x=>x.TokenHash).HasMaxLength(64).IsRequired(); e.HasIndex(x=>x.TokenHash).IsUnique(); e.HasIndex(x=>x.ExpiresAt); e.HasOne<AdminAccount>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade); });
        model.Entity<AdminAccount>(e => {e.HasKey(x=>x.Id); e.Property(x=>x.Email).HasMaxLength(320).IsRequired(); e.HasIndex(x=>x.Email).IsUnique(); e.Property(x=>x.PasswordHash).IsRequired(); e.Property(x=>x.DisplayName).HasMaxLength(100);});
    }
}
public sealed class AdminAccount {public Guid Id {get; set;} = Guid.NewGuid(); public string Email {get;set;} = ""; public string PasswordHash {get;set;} = ""; public string? DisplayName {get;set;} public Guid SessionVersion {get;set;} = Guid.NewGuid(); public DateTime? EmailVerifiedAt {get;set;} public bool RequiresEmailVerification {get;set;} }
public sealed class EmailVerificationToken { public Guid Id {get;set;} = Guid.NewGuid(); public Guid UserId {get;set;} public string TokenHash {get;set;} = ""; public DateTime ExpiresAt {get;set;} public DateTime? UsedAt {get;set;} }
public sealed class PasswordResetToken { public Guid Id {get;set;} = Guid.NewGuid(); public Guid UserId {get;set;} public string TokenHash {get;set;} = ""; public DateTime ExpiresAt {get;set;} public DateTime? UsedAt {get;set;} }
public sealed class EfTransactionRepository(FinanceDbContext db, IHttpContextAccessor context): ITransactionRepository
{
    private Guid CurrentUserId {
        get {
            var raw = context.HttpContext?.User.FindFirstValue("sub")
                ?? context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(raw, out var id) || id == Guid.Empty)
                throw new UnauthorizedAccessException("Sessão inválida. Entre novamente.");
            return id;
        }
    }
    public Task<List<Transaction>> GetMonthAsync(int year, int month, CancellationToken ct) {
        var userId = CurrentUserId;
        return db.Transactions.AsNoTracking().Where(x=>x.UserId==userId && x.Date.Year==year && x.Date.Month==month).ToListAsync(ct);
    }
    public Task<List<Transaction>> GetYearAsync(int year, CancellationToken ct) {
        var userId = CurrentUserId;
        return db.Transactions.AsNoTracking().Where(x=>x.UserId==userId && x.Date.Year==year).ToListAsync(ct);
    }
    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct) {
        var userId = CurrentUserId;
        return db.Transactions.FirstOrDefaultAsync(x=>x.Id==id && x.UserId==userId,ct);
    }
    public async Task AddAsync(Transaction t, CancellationToken ct) {
        t.AssignOwner(CurrentUserId);
        await db.Transactions.AddAsync(t,ct);
    }
    public void Remove(Transaction t) {
        if (t.UserId != CurrentUserId) throw new UnauthorizedAccessException("Acesso negado.");
        db.Transactions.Remove(t);
    }
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

