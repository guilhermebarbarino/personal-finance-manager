using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

internal sealed class AccountRateLimiter : IDisposable
{
    private static readonly IReadOnlyDictionary<string, RateLimitRule> Rules =
        new Dictionary<string, RateLimitRule>(StringComparer.Ordinal)
        {
            ["login-account"] = new(8, TimeSpan.FromMinutes(15), 15),
            ["registration-account"] = new(3, TimeSpan.FromHours(1), 12),
            ["recovery-account"] = new(3, TimeSpan.FromHours(1), 12),
            ["reset-token"] = new(5, TimeSpan.FromMinutes(15), 15),
            ["verification-token"] = new(5, TimeSpan.FromMinutes(15), 15),
            ["password-change-account"] = new(5, TimeSpan.FromMinutes(15), 15)
        };

    private readonly byte[] _hashKey;
    private readonly IReadOnlyDictionary<string, PartitionedRateLimiter<string>> _limiters;

    public AccountRateLimiter(string hashKey)
    {
        _hashKey = Encoding.UTF8.GetBytes(hashKey);
        _limiters = Rules.ToDictionary(
            pair => pair.Key,
            pair => PartitionedRateLimiter.Create<string, string>(resource =>
                RateLimitPartition.GetSlidingWindowLimiter(resource, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = pair.Value.PermitLimit,
                    Window = pair.Value.Window,
                    SegmentsPerWindow = pair.Value.Segments,
                    QueueLimit = 0,
                    AutoReplenishment = true
                })),
            StringComparer.Ordinal);
    }

    public ValueTask<RateLimitLease> AcquireAsync(
        string policy,
        string? identifier,
        CancellationToken cancellationToken)
    {
        if (!_limiters.TryGetValue(policy, out var limiter))
            throw new InvalidOperationException($"Política de rate limiting desconhecida: {policy}.");

        var normalized = string.IsNullOrWhiteSpace(identifier)
            ? "missing"
            : identifier.Trim().ToLowerInvariant();
        using var hmac = new HMACSHA256(_hashKey);
        var partition = Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"{policy}\0{normalized}")));
        return limiter.AcquireAsync(partition, permitCount: 1, cancellationToken);
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
            limiter.Dispose();
    }

    private sealed record RateLimitRule(int PermitLimit, TimeSpan Window, int Segments);
}

internal static class AccountRateLimitExtensions
{
    public static RouteHandlerBuilder RequireAccountRateLimit(
        this RouteHandlerBuilder builder,
        string policy,
        Func<EndpointFilterInvocationContext, string?> identifierFactory)
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var limiter = context.HttpContext.RequestServices.GetRequiredService<AccountRateLimiter>();
            using var lease = await limiter.AcquireAsync(
                policy,
                identifierFactory(context),
                context.HttpContext.RequestAborted);
            if (lease.IsAcquired)
                return await next(context);

            RateLimitResponse.SetRetryAfter(context.HttpContext.Response, lease);
            return Results.Json(
                new RateLimitError(RateLimitResponse.Message),
                statusCode: StatusCodes.Status429TooManyRequests);
        });
    }
}

internal static class RateLimitResponse
{
    public const string Message = "Muitas tentativas. Aguarde antes de tentar novamente.";

    public static void SetRetryAfter(HttpResponse response, RateLimitLease lease)
    {
        var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;
        response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
    }
}

internal sealed record RateLimitError(string Error);
