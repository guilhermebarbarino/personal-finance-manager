using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;

public sealed class EmailVerificationService(
    FinanceDbContext db, IHttpClientFactory clients, IConfiguration config,
    ILogger<EmailVerificationService> logger)
{
    private Uri? FrontendUri => Uri.TryCreate(config["EMAIL_VERIFICATION_FRONTEND_URL"], UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.Host == "localhost"))
        && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["RESEND_API_KEY"])
        && !string.IsNullOrWhiteSpace(config["RESEND_FROM_EMAIL"]) && FrontendUri is not null;

    public async Task<bool> SendAsync(AdminAccount account, CancellationToken ct)
    {
        if (!IsConfigured) return false;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var pending = new EmailVerificationToken {
            UserId = account.Id, TokenHash = Hash(token), ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        db.EmailVerificationTokens.Add(pending);
        await db.SaveChangesAsync(ct);
        var link = new UriBuilder(FrontendUri!) {Fragment="verify-email-token="+token}.Uri.ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["RESEND_API_KEY"]);
        request.Content = JsonContent.Create(new {
            from=config["RESEND_FROM_EMAIL"], to=new[]{account.Email},
            subject="Confirme seu e-mail — Meu Financeiro",
            text=$"Confirme seu e-mail para acessar sua conta: {link}\n\nO link expira em 30 minutos. Se não criou a conta, ignore esta mensagem."
        });
        try {
            using var response = await clients.CreateClient().SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return true;
            logger.LogWarning("Envio da confirmação de e-mail falhou com status {Status}", (int)response.StatusCode);
        }
        catch (HttpRequestException) { logger.LogWarning("Falha de conexão ao enviar confirmação de e-mail"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            logger.LogWarning("Tempo limite ao enviar confirmação de e-mail");
        }
        db.EmailVerificationTokens.Remove(pending);
        await db.SaveChangesAsync(ct);
        return false;
    }

    public async Task<bool> ConfirmAsync(string? token, CancellationToken ct)
    {
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit)) return false;
        var hash = Hash(token);
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var used = await db.EmailVerificationTokens
            .Where(x=>x.TokenHash==hash && x.UsedAt==null && x.ExpiresAt>now)
            .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UsedAt,now),ct);
        if (used != 1) { await transaction.RollbackAsync(ct); return false; }
        var pending = await db.EmailVerificationTokens.AsNoTracking().SingleAsync(x=>x.TokenHash==hash,ct);
        var updated = await db.AdminAccounts.Where(x=>x.Id==pending.UserId && x.EmailVerifiedAt==null)
            .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.EmailVerifiedAt,now)
                .SetProperty(x=>x.SessionVersion,Guid.NewGuid()),ct);
        if (updated != 1) { await transaction.RollbackAsync(ct); return false; }
        await db.EmailVerificationTokens.Where(x=>x.UserId==pending.UserId && x.UsedAt==null)
            .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UsedAt,now),ct);
        await db.PasswordResetTokens.Where(x=>x.UserId==pending.UserId && x.UsedAt==null)
            .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UsedAt,now),ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
