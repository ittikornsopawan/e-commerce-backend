using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ecommerce.Api.Tests;

// Spec: api-conventions.md "Limits and rate limiting": per client IP, 429 E90002,
// "Too many requests. Try again later.", Retry-After in seconds, answered before the handler runs.
public sealed class RateLimitTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient Client(int strictPerMinute) => factory.WithWebHostBuilder(b => b
        .UseSetting("Database:ConnectionString", "Host=127.0.0.1;Port=1;Database=x;Timeout=1")
        .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
        .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(new byte[32]))
        .UseSetting("RateLimit:StrictPerMinute", strictPerMinute.ToString())).CreateClient();

    // A body that fails validation, so the handler answers without touching the database.
    private static Task<HttpResponseMessage> Login(HttpClient client) =>
        client.PostAsJsonAsync("/v1/auth/login", new { usernameOrEmail = "", password = "" });

    [Fact]
    public async Task A_public_api_answers_429_E90002_in_the_envelope_with_Retry_After_over_the_limit()
    {
        var client = Client(strictPerMinute: 3);
        for (var i = 0; i < 3; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Login(client)).StatusCode);
        }

        var response = await Login(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var retryAfter = int.Parse(response.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retryAfter, 1, 60);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = json.RootElement.GetProperty("status");
        Assert.Equal(429, status.GetProperty("code").GetInt32());
        Assert.Equal("Too Many Requests", status.GetProperty("message").GetString());
        Assert.Equal("E90002", status.GetProperty("bizCode").GetString());
        Assert.Equal("Too many requests. Try again later.", status.GetProperty("bizMessage").GetString());
        Assert.Equal("ERROR", status.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_request_over_the_limit_never_reaches_the_handler()
    {
        var client = Client(strictPerMinute: 1);
        await Login(client);

        var response = await client.PostAsync("/v1/auth/login", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));

        // A malformed body is a 400 from model binding; 429 proves the limiter ran before binding.
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task An_api_that_is_not_public_is_not_limited()
    {
        var client = Client(strictPerMinute: 1);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(JwtVerificationTests.ProfileProbe())).StatusCode);
        }
    }
}
