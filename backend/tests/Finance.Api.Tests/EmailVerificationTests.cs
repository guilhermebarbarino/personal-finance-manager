using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Finance.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public sealed class EmailVerificationTests
{
    [Fact]
    public async Task Upgrade_preserves_existing_accounts_and_new_accounts_require_single_use_confirmation()
    {
        var connection = Environment.GetEnvironmentVariable("FINANCE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Configure FINANCE_TEST_DATABASE com um PostgreSQL exclusivo para testes.");
        // The CI supplies a dedicated database; never point this suite at production.
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connection).Options;
        var legacy = new AdminAccount {Email="legacy@example.com",DisplayName="Nome existente"};
        const string password = "A-test-password-123!";
        legacy.PasswordHash = new PasswordHasher<AdminAccount>().HashPassword(legacy,password);
        await using (var db = new FinanceDbContext(options)) {
            await db.Database.EnsureCreatedAsync();
            db.AdminAccounts.Add(legacy); await db.SaveChangesAsync();
            // Simulate the prior schema before the application applies the additive upgrade.
            await db.Database.ExecuteSqlRawAsync("""
                DROP TABLE "EmailVerificationTokens";
                ALTER TABLE "AdminAccounts" DROP COLUMN "EmailVerifiedAt";
                ALTER TABLE "AdminAccounts" DROP COLUMN "RequiresEmailVerification";
                """);
        }
        var mail = new FakeMail();
        using var factory = new ApiFactory(connection,mail);
        using var client = factory.CreateClient();
        using var legacyLogin = await client.PostAsJsonAsync("/api/auth/login",new {email=legacy.Email,password});
        Assert.Equal(HttpStatusCode.OK,legacyLogin.StatusCode);
        var legacyJwt = (await legacyLogin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get,"/api/me");
        profileRequest.Headers.Authorization = new("Bearer",legacyJwt);
        using var profile = await client.SendAsync(profileRequest);
        Assert.Equal("Nome existente",(await profile.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayName").GetString());

        const string email = "new@example.com";
        using var signup = await client.PostAsJsonAsync("/api/auth/register",new {email,password,displayName="Nova conta"});
        Assert.Equal(HttpStatusCode.Created,signup.StatusCode);
        Assert.True((await signup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requiresEmailVerification").GetBoolean());
        var firstToken = mail.LastToken;
        using var pendingLogin = await client.PostAsJsonAsync("/api/auth/login",new {email,password});
        Assert.Equal(HttpStatusCode.Forbidden,pendingLogin.StatusCode);
        using var forgot = await client.PostAsJsonAsync("/api/auth/forgot-password",new {email});
        Assert.Equal(HttpStatusCode.OK,forgot.StatusCode);
        Assert.Equal(1,mail.Sent);

        using var unknown = await client.PostAsJsonAsync("/api/auth/resend-verification",new {email="unknown@example.com"});
        using var resend = await client.PostAsJsonAsync("/api/auth/resend-verification",new {email});
        Assert.Equal(await unknown.Content.ReadAsStringAsync(),await resend.Content.ReadAsStringAsync());
        Assert.Equal(2,mail.Sent);
        var token = mail.LastToken;
        await using (var db = new FinanceDbContext(options)) {
            var account = await db.AdminAccounts.SingleAsync(x=>x.Email==email);
            Assert.Null(account.EmailVerifiedAt);
            Assert.True(account.RequiresEmailVerification);
            var forgedPendingJwt = new JwtSecurityToken("finance-api","finance-web",new[]{
                new Claim("sub",account.Id.ToString()),new Claim("session_version",account.SessionVersion.ToString())
            },expires:DateTime.UtcNow.AddMinutes(5),signingCredentials:new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k',64))),SecurityAlgorithms.HmacSha256));
            using var protectedRequest = new HttpRequestMessage(HttpMethod.Get,"/api/me");
            protectedRequest.Headers.Authorization=new("Bearer",new JwtSecurityTokenHandler().WriteToken(forgedPendingJwt));
            using var protectedResponse = await client.SendAsync(protectedRequest);
            Assert.Equal(HttpStatusCode.Unauthorized,protectedResponse.StatusCode);
            Assert.Empty(await db.PasswordResetTokens.ToListAsync());
            Assert.DoesNotContain(await db.EmailVerificationTokens.Select(x=>x.TokenHash).ToListAsync(),x=>x==token);
            var expired = await db.EmailVerificationTokens.SingleAsync(x=>x.TokenHash==Hash(firstToken));
            expired.ExpiresAt=DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        }
        using var invalid = await client.PostAsJsonAsync("/api/auth/verify-email",new {token="invalid"});
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using var expiredResponse = await client.PostAsJsonAsync("/api/auth/verify-email",new {token=firstToken});
        Assert.Equal(HttpStatusCode.BadRequest,expiredResponse.StatusCode);
        using var confirm = await client.PostAsJsonAsync("/api/auth/verify-email",new {token});
        Assert.Equal(HttpStatusCode.OK,confirm.StatusCode);
        using var replay = await client.PostAsJsonAsync("/api/auth/verify-email",new {token});
        Assert.Equal(HttpStatusCode.BadRequest,replay.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/auth/login",new {email,password});
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        await using (var db = new FinanceDbContext(options)) {
            Assert.NotNull((await db.AdminAccounts.SingleAsync(x=>x.Email==email)).EmailVerifiedAt);
            Assert.All(await db.EmailVerificationTokens.ToListAsync(),x=>Assert.NotNull(x.UsedAt));
            var existing = await db.AdminAccounts.SingleAsync(x=>x.Id==legacy.Id);
            Assert.False(existing.RequiresEmailVerification);
            Assert.Null(existing.EmailVerifiedAt);
            Assert.Equal(legacy.PasswordHash,existing.PasswordHash);
            // Sending failure keeps the account pending and removes only the failed token.
            var pending = new AdminAccount {Email="delivery-failure@example.com",RequiresEmailVerification=true};
            db.AdminAccounts.Add(pending);await db.SaveChangesAsync();
            mail.Fail=true;
            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<EmailVerificationService>();
            Assert.False(await service.SendAsync(pending,CancellationToken.None));
            Assert.Empty(await db.EmailVerificationTokens.Where(x=>x.UserId==pending.Id).ToListAsync());
            mail.Fail=false;
            Assert.True(await service.SendAsync(pending,CancellationToken.None));
            Assert.Single(await db.EmailVerificationTokens.Where(x=>x.UserId==pending.Id).ToListAsync());
        }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private sealed class ApiFactory(string connection,FakeMail mail) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseSetting("ConnectionStrings:Default",connection);
            builder.UseSetting("JWT_KEY",new string('k',64));
            builder.UseSetting("ADMIN_EMAIL","seed@example.com");
            builder.UseSetting("ADMIN_PASSWORD","A-seed-password-123!");
            builder.UseSetting("RESEND_API_KEY","fake-key");
            builder.UseSetting("RESEND_FROM_EMAIL","noreply@example.com");
            builder.UseSetting("EMAIL_VERIFICATION_FRONTEND_URL","https://example.com/finance/");
            builder.UseSetting("PASSWORD_RESET_FRONTEND_URL","https://example.com/finance/");
            builder.ConfigureServices(services=>{
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(mail);
            });
        }
    }
    private sealed class FakeMail : HttpMessageHandler,IHttpClientFactory
    {
        public int Sent {get;private set;}
        public string LastToken {get;private set;} = "";
        public bool Fail {get;set;}
        public HttpClient CreateClient(string name) => new(this,disposeHandler:false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            Assert.Equal("https://api.resend.com/emails",request.RequestUri!.ToString());
            var payload=await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            var text=payload.GetProperty("text").GetString()!;
            Assert.Contains("https://example.com/finance/#verify-email-token=",text);
            LastToken=text.Split("verify-email-token=")[1][..64];
            Sent++;
            return new(Fail?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK);
        }
    }
}
