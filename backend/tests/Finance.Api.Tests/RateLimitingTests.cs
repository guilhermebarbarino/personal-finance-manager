using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task Sensitive_endpoints_limit_by_forwarded_ip_and_normalized_identifier()
    {
        var connection = Environment.GetEnvironmentVariable("FINANCE_TEST_DATABASE")
            ?? throw new InvalidOperationException("Configure FINANCE_TEST_DATABASE com um PostgreSQL exclusivo para testes.");
        using var factory = new ApiFactory(connection);
        using var client = factory.CreateClient();

        // Distinct proxy-provided IPs do not bypass the account-level login limit.
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            using var response = await PostAsync(
                client,
                "/api/auth/login",
                new {email=" Target@Example.com ",password="incorrect-password"},
                $"203.0.113.{attempt}");
            Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
        }
        using (var blocked = await PostAsync(
            client,
            "/api/auth/login",
            new {email="target@example.com",password="incorrect-password"},
            "203.0.113.20"))
        {
            await AssertRateLimitedAsync(blocked);
        }
        using (var independent = await PostAsync(
            client,
            "/api/auth/login",
            new {email="other@example.com",password="incorrect-password"},
            "203.0.113.21"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized,independent.StatusCode);
        }

        // One IP cannot rotate account identifiers to evade the endpoint limit.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await PostAsync(
                client,
                "/api/auth/login",
                new {email=$"rotated-{attempt}@example.com",password="incorrect-password"},
                "198.51.100.10");
            Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
        }
        using (var blocked = await PostAsync(
            client,
            "/api/auth/login",
            new {email="rotated-6@example.com",password="incorrect-password"},
            "198.51.100.10"))
        {
            await AssertRateLimitedAsync(blocked);
        }

        // Forgot-password and resend-verification share an account recovery budget.
        using (var first = await PostAsync(client,"/api/auth/forgot-password",new {email="victim@example.com"},"192.0.2.1"))
            Assert.Equal(HttpStatusCode.OK,first.StatusCode);
        using (var second = await PostAsync(client,"/api/auth/resend-verification",new {email="VICTIM@example.com"},"192.0.2.2"))
            Assert.Equal(HttpStatusCode.OK,second.StatusCode);
        using (var third = await PostAsync(client,"/api/auth/forgot-password",new {email=" victim@example.com "},"192.0.2.3"))
            Assert.Equal(HttpStatusCode.OK,third.StatusCode);
        using (var blocked = await PostAsync(client,"/api/auth/resend-verification",new {email="victim@example.com"},"192.0.2.4"))
            await AssertRateLimitedAsync(blocked);

        // Token attempts also remain bounded when the caller rotates IPs.
        var token = new string('a',64);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await PostAsync(
                client,
                "/api/auth/reset-password",
                new {token,newPassword="A-new-password-123!"},
                $"198.18.0.{attempt}");
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        }
        using (var blocked = await PostAsync(
            client,
            "/api/auth/reset-password",
            new {token,newPassword="A-new-password-123!"},
            "198.18.0.20"))
        {
            await AssertRateLimitedAsync(blocked);
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client,string path,object body,string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,path) {Content=JsonContent.Create(body)};
        request.Headers.Add("X-Forwarded-For",ip);
        return await client.SendAsync(request);
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests,response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After",out var values));
        Assert.True(int.TryParse(values.Single(),out var seconds));
        Assert.True(seconds>0);
        var payload=await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Muitas tentativas. Aguarde antes de tentar novamente.",payload.GetProperty("error").GetString());
    }

    private sealed class ApiFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default",connection);
            builder.UseSetting("JWT_KEY",new string('r',64));
            builder.UseSetting("ADMIN_EMAIL","rate-limit-seed@example.com");
            builder.UseSetting("ADMIN_PASSWORD","A-seed-password-123!");
        }
    }
}
