using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Finance.Application;
using Finance.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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
builder.Services.AddScoped<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();
builder.Services.AddCors(o=>o.AddPolicy("frontend",p=>p.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters {
        ValidateIssuer=true, ValidIssuer="finance-api", ValidateAudience=true, ValidAudience="finance-web",
        ValidateLifetime=true, ValidateIssuerSigningKey=true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ClockSkew=TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o=>{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _=>new FixedWindowRateLimiterOptions { PermitLimit=5, Window=TimeSpan.FromMinutes(1), QueueLimit=0, AutoReplenishment=true }));
    o.AddPolicy("register",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _=>new FixedWindowRateLimiterOptions { PermitLimit=3, Window=TimeSpan.FromHours(1), QueueLimit=0, AutoReplenishment=true }));
});
var app = builder.Build();
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.AdminAccounts.AnyAsync()) {
        var admin = new AdminAccount {Email=adminEmail.Trim().ToLowerInvariant()};
        admin.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminAccount>>().HashPassword(admin,adminPassword);
        db.AdminAccounts.Add(admin); await db.SaveChangesAsync();
    }
}
app.UseCors("frontend"); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/health",()=>Results.Ok(new {status="ok"}));
app.MapPost("/api/auth/login", async (LoginRequest login, FinanceDbContext db, IPasswordHasher<AdminAccount> hasher) => {
    var account = await db.AdminAccounts.SingleOrDefaultAsync(x=>x.Email==login.Email.Trim().ToLower());
    if (account is null || hasher.VerifyHashedPassword(account,account.PasswordHash,login.Password) == PasswordVerificationResult.Failed)
        return Results.Unauthorized();
    var until=DateTime.UtcNow.AddHours(2);
    var jwt = new JwtSecurityToken("finance-api", "finance-web",new[] { new Claim(JwtRegisteredClaimNames.Sub,account.Id.ToString()),new Claim(ClaimTypes.Role,"admin") },expires:until,signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
    return Results.Ok(new {token=new JwtSecurityTokenHandler().WriteToken(jwt),expiresAt=until});
}).RequireRateLimiting("login");
app.MapPost("/api/auth/register", async (RegisterRequest input, FinanceDbContext db, IPasswordHasher<AdminAccount> hasher, CancellationToken ct) => {
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
    var account = new AdminAccount { Email=email, DisplayName=string.IsNullOrWhiteSpace(displayName)?null:displayName };
    account.PasswordHash=hasher.HashPassword(account,input.Password);
    db.AdminAccounts.Add(account);
    try {await db.SaveChangesAsync(ct);}
    catch (DbUpdateException) {return Results.Conflict(new {error="Não foi possível criar a conta. Confira se o e-mail já está em uso."});}
    return Results.Created("/api/auth/login",new {message="Conta criada. Faça login para continuar."});
}).RequireRateLimiting("register");
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
