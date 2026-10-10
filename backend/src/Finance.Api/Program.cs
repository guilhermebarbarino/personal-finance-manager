using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Finance.Application;
using Finance.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default obrigatório.");
var key = builder.Configuration["JWT_KEY"] ?? throw new InvalidOperationException("JWT_KEY obrigatório.");
if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("JWT_KEY deve ter ao menos 32 bytes.");
var adminEmail = builder.Configuration["ADMIN_EMAIL"] ?? throw new InvalidOperationException("ADMIN_EMAIL obrigatório.");
var adminPassword = builder.Configuration["ADMIN_PASSWORD"] ?? throw new InvalidOperationException("ADMIN_PASSWORD obrigatório.");
if (adminPassword.Length < 12) throw new InvalidOperationException("ADMIN_PASSWORD deve ter ao menos 12 caracteres.");
var corsOrigin = builder.Configuration["CORS_ORIGIN"] ?? "http://localhost:5173";
builder.Services.AddDbContext<FinanceDbContext>(o=>o.UseNpgsql(connection));
builder.Services.AddScoped<ITransactionRepository, EfTransactionRepository>();
builder.Services.AddScoped<FinanceService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<EmailVerificationService>();
builder.Services.AddSingleton(new AccountRateLimiter(key));
builder.Services.AddScoped<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();
builder.Services.AddCors(o=>o.AddPolicy("frontend",p=>p.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod()));
builder.Services.Configure<ForwardedHeadersOptions>(o=>{
    // Render terminates TLS at its proxy. Trust only the nearest forwarded hop.
    o.ForwardedHeaders=ForwardedHeaders.XForwardedFor|ForwardedHeaders.XForwardedProto;
    o.ForwardLimit=1;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{
    o.MapInboundClaims = false;
    o.Events = new JwtBearerEvents {OnTokenValidated = async context => {
        var idText = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var version = context.Principal?.FindFirstValue("session_version");
        if (!Guid.TryParse(idText,out var userId) || !Guid.TryParse(version,out var tokenVersion)) {context.Fail("Sessão inválida");return;}
        var db = context.HttpContext.RequestServices.GetRequiredService<FinanceDbContext>();
        var current = await db.AdminAccounts.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==userId);
        if (current is null || current.SessionVersion!=tokenVersion || (current.RequiresEmailVerification && current.EmailVerifiedAt is null)) context.Fail("Sessão revogada");
    }};
    o.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer=true, ValidIssuer="finance-api", ValidateAudience=true, ValidAudience="finance-web",
        ValidateLifetime=true, ValidateIssuerSigningKey=true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ClockSkew=TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o=>{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected=async (context,ct)=>{
        RateLimitResponse.SetRetryAfter(context.HttpContext.Response,context.Lease);
        await context.HttpContext.Response.WriteAsJsonAsync(
            new RateLimitError(RateLimitResponse.Message),
            cancellationToken:ct);
    };
    o.AddPolicy("login",ctx=>RateLimitPartition.GetSlidingWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _=>new SlidingWindowRateLimiterOptions { PermitLimit=5, Window=TimeSpan.FromMinutes(1), SegmentsPerWindow=6, QueueLimit=0, AutoReplenishment=true }));
    o.AddPolicy("password-reset",ctx=>RateLimitPartition.GetSlidingWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _=>new SlidingWindowRateLimiterOptions { PermitLimit=3, Window=TimeSpan.FromMinutes(15), SegmentsPerWindow=15, QueueLimit=0, AutoReplenishment=true }));
    o.AddPolicy("email-confirmation",ctx=>RateLimitPartition.GetSlidingWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _=>new SlidingWindowRateLimiterOptions { PermitLimit=10, Window=TimeSpan.FromMinutes(15), SegmentsPerWindow=15, QueueLimit=0, AutoReplenishment=true }));
    o.AddPolicy("register",ctx=>RateLimitPartition.GetSlidingWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _=>new SlidingWindowRateLimiterOptions { PermitLimit=3, Window=TimeSpan.FromHours(1), SegmentsPerWindow=12, QueueLimit=0, AutoReplenishment=true }));
});
var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    await db.Database.EnsureCreatedAsync();
    // Stage 1 only: additive, repeatable upgrade for existing installations.
    // Replace the overall EnsureCreated strategy in the separate migrations stage.
    using var migrationStream = typeof(EmailVerificationService).Assembly.GetManifestResourceStream("EmailVerification.sql")
        ?? throw new InvalidOperationException("Migração de verificação de e-mail ausente.");
    using var migrationReader = new StreamReader(migrationStream);
    await db.Database.ExecuteSqlRawAsync(await migrationReader.ReadToEndAsync());
    if (!await db.AdminAccounts.AnyAsync()) {
        var admin = new AdminAccount {Email=adminEmail.Trim().ToLowerInvariant()};
        admin.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminAccount>>().HashPassword(admin,adminPassword);
        db.AdminAccounts.Add(admin); await db.SaveChangesAsync();
    }
}
app.UseForwardedHeaders(); app.UseCors("frontend"); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health",()=>Results.Ok(new {status="ok"}));
app.MapPost("/api/auth/login", async (LoginRequest login, FinanceDbContext db, IPasswordHasher<AdminAccount> hasher) => {
    var account = await db.AdminAccounts.SingleOrDefaultAsync(x=>x.Email==login.Email.Trim().ToLower());
    if (account is null || hasher.VerifyHashedPassword(account,account.PasswordHash,login.Password) == PasswordVerificationResult.Failed)
        return Results.Unauthorized();
    if (account.RequiresEmailVerification && account.EmailVerifiedAt is null)
        return Results.Json(new {error="Confirme seu e-mail antes de entrar.",code="email_verification_required"},statusCode:403);
    var until=DateTime.UtcNow.AddHours(2);
    var jwt = new JwtSecurityToken("finance-api", "finance-web",new[] { new Claim(JwtRegisteredClaimNames.Sub,account.Id.ToString()),new Claim(ClaimTypes.Role,"admin"),new Claim("session_version",account.SessionVersion.ToString()) },expires:until,signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
    return Results.Ok(new {token=new JwtSecurityTokenHandler().WriteToken(jwt),expiresAt=until});
}).RequireRateLimiting("login")
  .RequireAccountRateLimit("login-account",ctx=>ctx.Arguments.OfType<LoginRequest>().FirstOrDefault()?.Email);
app.MapPost("/api/auth/register", async (RegisterRequest input, FinanceDbContext db, IPasswordHasher<AdminAccount> hasher, EmailVerificationService verification, CancellationToken ct) => {
    var email = input.Email?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(email) || email.Length > 320 ||
        !System.Net.Mail.MailAddress.TryCreate(email, out var mail) ||
        !string.Equals(mail.Address, email, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new {error="Informe um e-mail válido."});
    if (string.IsNullOrEmpty(input.Password) || input.Password.Length < 12 || input.Password.Length > 128)
        return Results.BadRequest(new {error="A senha deve conter entre 12 e 128 caracteres."});
    if (await db.AdminAccounts.AnyAsync(x=>x.Email==email,ct))
        return Results.Conflict(new {error="Já existe uma conta com este e-mail."});
    var displayName=input.DisplayName?.Trim();
    if (displayName?.Length > 100) return Results.BadRequest(new {error="O nome deve ter até 100 caracteres."});
    if (!verification.IsConfigured) return Results.Problem("O cadastro está temporariamente indisponível. Tente novamente mais tarde.",statusCode:503);
    var account = new AdminAccount { Email=email, RequiresEmailVerification=true, DisplayName=string.IsNullOrWhiteSpace(displayName)?null:displayName };
    account.PasswordHash=hasher.HashPassword(account,input.Password);
    db.AdminAccounts.Add(account);
    try {await db.SaveChangesAsync(ct);}
    catch (DbUpdateException e) when (e.InnerException is PostgresException {SqlState: PostgresErrorCodes.UniqueViolation}) {
        return Results.Conflict(new {error="Já existe uma conta com este e-mail."});
    }
    catch (DbUpdateException) {
        return Results.Problem("Não foi possível criar a conta devido a um erro no banco de dados.",statusCode:500);
    }
    var sent = await verification.SendAsync(account,ct);
    return Results.Created("/api/auth/login",new {message=sent
        ? "Conta criada. Confirme seu e-mail pelo link enviado antes de entrar."
        : "Conta criada, mas não foi possível enviar a confirmação. Solicite um novo link.",requiresEmailVerification=true});
}).RequireRateLimiting("register")
  .RequireAccountRateLimit("registration-account",ctx=>ctx.Arguments.OfType<RegisterRequest>().FirstOrDefault()?.Email);
// Password recovery: uniform response prevents account enumeration.
app.MapPost("/api/auth/forgot-password", async (ForgotPasswordRequest input, FinanceDbContext db, IHttpClientFactory clients, IConfiguration config, CancellationToken ct) => {
    var message = new {message="Se existir uma conta com esse e-mail, enviaremos um link para redefinir a senha."};
    var email = input.Email?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(email) || email.Length > 320) return Results.Ok(message);
    var account = await db.AdminAccounts.SingleOrDefaultAsync(x=>x.Email==email,ct);
    if (account is null || (account.RequiresEmailVerification && account.EmailVerifiedAt is null)) return Results.Ok(message);
    var apiKey = config["RESEND_API_KEY"];
    var from = config["RESEND_FROM_EMAIL"];
    var baseUrl = config["PASSWORD_RESET_FRONTEND_URL"];
    if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(from) ||
        !Uri.TryCreate(baseUrl,UriKind.Absolute,out var baseUri) ||
        (baseUri.Scheme!="https" && !(baseUri.Host=="localhost" && baseUri.Scheme=="http")))
        return Results.Ok(message);
    // Do not log plaintext tokens or expose them in HTTP responses.
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    var reset = new PasswordResetToken {UserId=account.Id,TokenHash=hash,ExpiresAt=DateTime.UtcNow.AddMinutes(20)};
    db.PasswordResetTokens.Add(reset);
    await db.SaveChangesAsync(ct);
    var link = new UriBuilder(baseUri) {Fragment="reset-token="+Uri.EscapeDataString(token)}.Uri.ToString();
    var client = clients.CreateClient();
    using var request = new HttpRequestMessage(HttpMethod.Post,"https://api.resend.com/emails");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",apiKey);
    request.Content=JsonContent.Create(new {from,to=new[]{account.Email},subject="Redefinir senha — Meu Financeiro",
        text=$"Recebemos uma solicitação para redefinir sua senha. Acesse o link (válido por 20 minutos): {link}\\n\\nSe não solicitou, ignore esta mensagem."});
    try {
        using var response = await client.SendAsync(request,ct);
        if (!response.IsSuccessStatusCode) {
            db.PasswordResetTokens.Remove(reset); await db.SaveChangesAsync(ct);
            return Results.Ok(message);
        }
    } catch (HttpRequestException) {
        db.PasswordResetTokens.Remove(reset); await db.SaveChangesAsync(ct);
        return Results.Ok(message);
    }
    return Results.Ok(message);
}).RequireRateLimiting("password-reset")
  .RequireAccountRateLimit("recovery-account",ctx=>ctx.Arguments.OfType<ForgotPasswordRequest>().FirstOrDefault()?.Email);

app.MapPost("/api/auth/reset-password", async (ResetPasswordRequest input, FinanceDbContext db, IPasswordHasher<AdminAccount> hasher, CancellationToken ct) => {
    if (string.IsNullOrEmpty(input.NewPassword) || input.NewPassword.Length<12 || input.NewPassword.Length>128)
        return Results.BadRequest(new {error="A senha deve conter entre 12 e 128 caracteres."});
    if (string.IsNullOrWhiteSpace(input.Token) || input.Token.Length!=64 ||
        !input.Token.All(Uri.IsHexDigit))
        return Results.BadRequest(new {error="Link inválido ou expirado."});
    var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.Token))).ToLowerInvariant();
    await using var transaction=await db.Database.BeginTransactionAsync(ct);
    var used=await db.PasswordResetTokens
        .Where(x=>x.TokenHash==hash && x.UsedAt==null && x.ExpiresAt>DateTime.UtcNow)
        .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UsedAt,DateTime.UtcNow),ct);
    if (used!=1) {await transaction.RollbackAsync(ct);return Results.BadRequest(new {error="Link inválido ou expirado."});}
    var reset=await db.PasswordResetTokens.AsNoTracking().SingleAsync(x=>x.TokenHash==hash,ct);
    var account=await db.AdminAccounts.SingleOrDefaultAsync(x=>x.Id==reset.UserId,ct);
    if (account is null || (account.RequiresEmailVerification && account.EmailVerifiedAt is null)) {await transaction.RollbackAsync(ct);return Results.BadRequest(new {error="Link inválido ou expirado."});}
    account.PasswordHash=hasher.HashPassword(account,input.NewPassword);
    account.SessionVersion=Guid.NewGuid(); // Invalidates all previously issued JWTs.
    await db.PasswordResetTokens.Where(x=>x.UserId==account.Id && x.UsedAt==null)
        .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UsedAt,DateTime.UtcNow),ct);
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.Ok(new {message="Senha redefinida. Faça login novamente."});
}).RequireRateLimiting("password-reset")
  .RequireAccountRateLimit("reset-token",ctx=>ctx.Arguments.OfType<ResetPasswordRequest>().FirstOrDefault()?.Token);

app.MapPost("/api/auth/resend-verification", async (ForgotPasswordRequest input, FinanceDbContext db, EmailVerificationService verification, CancellationToken ct) => {
    var email = input.Email?.Trim().ToLowerInvariant();
    var account = string.IsNullOrWhiteSpace(email) || email.Length>320 ? null
        : await db.AdminAccounts.SingleOrDefaultAsync(x=>x.Email==email,ct);
    if (account is not null && account.EmailVerifiedAt is null && verification.IsConfigured)
        await verification.SendAsync(account,ct);
    return Results.Ok(new {message="Se houver uma conta pendente, enviaremos um link de confirmação."});
}).RequireRateLimiting("password-reset")
  .RequireAccountRateLimit("recovery-account",ctx=>ctx.Arguments.OfType<ForgotPasswordRequest>().FirstOrDefault()?.Email);
app.MapPost("/api/auth/verify-email", async Task<IResult> (VerifyEmailRequest input, EmailVerificationService verification, CancellationToken ct) =>
    await verification.ConfirmAsync(input.Token,ct)
        ? Results.Ok(new {message="E-mail confirmado. Faça login para continuar."})
        : Results.BadRequest(new {error="Link inválido ou expirado. Solicite uma nova confirmação."}))
    .RequireRateLimiting("email-confirmation")
    .RequireAccountRateLimit("verification-token",ctx=>ctx.Arguments.OfType<VerifyEmailRequest>().FirstOrDefault()?.Token);

var auth = app.MapGroup("/api").RequireAuthorization();
auth.MapGet("/me", async (ClaimsPrincipal principal, FinanceDbContext db, CancellationToken ct) => {
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return Results.Unauthorized();
    var account=await db.AdminAccounts.AsNoTracking().Where(x=>x.Id==userId)
        .Select(x=>new {x.Email,x.DisplayName}).SingleOrDefaultAsync(ct);
    return account is null ? Results.Unauthorized() : Results.Ok(new {email=account.Email,displayName=account.DisplayName});
});
auth.MapPut("/me", async (UpdateProfileRequest input, ClaimsPrincipal principal, FinanceDbContext db, CancellationToken ct) => {
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return Results.Unauthorized();
    var name=input.DisplayName?.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length>100)
        return Results.BadRequest(new {error="Informe um nome entre 1 e 100 caracteres."});
    var account=await db.AdminAccounts.SingleOrDefaultAsync(x=>x.Id==userId,ct);
    if (account is null) return Results.Unauthorized();
    account.DisplayName=name;
    await db.SaveChangesAsync(ct);
    return Results.Ok(new {displayName=account.DisplayName,email=account.Email});
});
auth.MapPost("/me/change-password", async (ChangePasswordRequest input, ClaimsPrincipal principal, FinanceDbContext db, IPasswordHasher<AdminAccount> hasher, CancellationToken ct) => {
    if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return Results.Unauthorized();
    if (string.IsNullOrEmpty(input.NewPassword) || input.NewPassword.Length < 12 || input.NewPassword.Length > 128)
        return Results.BadRequest(new {error="A nova senha deve conter entre 12 e 128 caracteres."});
    if (input.NewPassword != input.ConfirmPassword)
        return Results.BadRequest(new {error="A confirmação não corresponde à nova senha."});
    var account = await db.AdminAccounts.SingleOrDefaultAsync(x=>x.Id==userId,ct);
    if (account is null) return Results.Unauthorized();
    if (hasher.VerifyHashedPassword(account,account.PasswordHash,input.CurrentPassword ?? "") == PasswordVerificationResult.Failed)
        return Results.BadRequest(new {error="Senha atual incorreta."});
    if (hasher.VerifyHashedPassword(account,account.PasswordHash,input.NewPassword) != PasswordVerificationResult.Failed)
        return Results.BadRequest(new {error="Escolha uma senha diferente da atual."});
    account.PasswordHash=hasher.HashPassword(account,input.NewPassword);
    account.SessionVersion=Guid.NewGuid();
    await db.SaveChangesAsync(ct);
    return Results.Ok(new {message="Senha alterada. Entre novamente."});
}).RequireRateLimiting("password-reset")
  .RequireAccountRateLimit("password-change-account",ctx=>ctx.HttpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub));
auth.MapGet("/transactions", async (string? month, FinanceService svc, CancellationToken ct)=> {
    if (!DateOnly.TryParseExact((month ?? DateTime.UtcNow.ToString("yyyy-MM"))+"-01", "yyyy-MM-dd", out var date)) return Results.BadRequest(new {error="Formato de mês inválido. Use yyyy-MM."});
    try { return Results.Ok(await svc.ListAsync(date.Year,date.Month,ct)); } catch (ArgumentException e) {return Results.BadRequest(new {error=e.Message});}
});
auth.MapPost("/transactions",async (TransactionInput input,FinanceService svc,CancellationToken ct)=>{
    try {var created=await svc.CreateAsync(input,ct); return Results.Created($"/api/transactions/{created.Id}",created);}catch(ArgumentException e){return Results.BadRequest(new {error=e.Message});}
});
auth.MapPut("/transactions/{id:guid}",async (Guid id,TransactionInput input,FinanceService svc,CancellationToken ct)=>{
    try {var updated=await svc.UpdateAsync(id,input,ct);return updated is null?Results.NotFound():Results.Ok(updated);}catch(ArgumentException e){return Results.BadRequest(new {error=e.Message});}
});
auth.MapDelete("/transactions/{id:guid}",async(Guid id, FinanceService svc,CancellationToken ct)=>await svc.DeleteAsync(id,ct)?Results.NoContent():Results.NotFound());
auth.MapGet("/dashboard",async(int? year,FinanceService svc,CancellationToken ct)=>{
    try{return Results.Ok(await svc.DashboardAsync(year??DateTime.UtcNow.Year,ct));}catch(ArgumentException e){return Results.BadRequest(new {error=e.Message});}
});
app.Run();
record LoginRequest(string Email, string Password);
record RegisterRequest(string Email, string Password, string? DisplayName = null);
record UpdateProfileRequest(string DisplayName);
record ChangePasswordRequest(string CurrentPassword,string NewPassword,string ConfirmPassword);

record ForgotPasswordRequest(string Email);
record ResetPasswordRequest(string Token,string NewPassword);


record VerifyEmailRequest(string Token);
public partial class Program {}
