using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/authen-and-author/03-reset-password/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class ResetPasswordApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/auth/reset-password";
    private const string Generic = "If the email address is registered, a reset link has been sent.";
    private const string ResetUrl = "https://reset.test/reset-password?token=";
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly RecordingEmailSender _emails = new();
    private readonly FieldProtector _protector = new(FieldKey);

    private sealed class RecordingEmailSender : IEmailSender
    {
        public ConcurrentQueue<(string To, string Link)> Sent { get; } = new();
        public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken ct)
        {
            Sent.Enqueue((toEmail, resetLink));
            return Task.CompletedTask;
        }
    }

    public ResetPasswordApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey))
            .UseSetting("Security:PasswordResetUrl", ResetUrl)
            .UseSetting("RateLimit:StrictPerMinute", "10000")
            .ConfigureTestServices(s => s.AddSingleton<IEmailSender>(_emails)));
    }

    private async Task<(Guid Id, string Email)> UserAsync()
    {
        var registration = Registration();
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return (await new RegistrationRepository(db, _protector).AddAsync(registration, CancellationToken.None), registration.EmailAddress);
    }

    private Task<HttpResponseMessage> Send(object body, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        return (client ?? _factory.CreateClient()).SendAsync(request);
    }

    private static async Task<JsonElement> Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement.Clone();
        Assert.Equal(code, (int)response.StatusCode);
        var status = root.GetProperty("status");
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        Assert.False(root.TryGetProperty("data", out _));
        return root;
    }

    private static async Task Generic200(HttpResponseMessage response)
    {
        var status = (await Status(response, 200, "S10000", Generic)).GetProperty("status");
        Assert.Equal(("Ok", "SUCCESS"), (status.GetProperty("message").GetString(), status.GetProperty("status").GetString()));
    }

    private async Task<List<PasswordResetRecord>> Rows(Guid userId)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return await db.PasswordResets.Where(r => r.UserId == userId).OrderBy(r => r.CreatedAt).ToListAsync();
    }

    private List<(string To, string Link)> SentTo(string email) => _emails.Sent.Where(e => e.To == email).ToList();

    [Fact]
    public async Task TC_RESET_001_issuance_for_an_active_user()
    {
        var (id, email) = await UserAsync();
        var before = DateTime.UtcNow;

        await Generic200(await Send(new { emailAddress = email }));

        var row = Assert.Single(await Rows(id));
        var (to, link) = Assert.Single(SentTo(email));
        Assert.StartsWith(ResetUrl, link);
        var token = link[ResetUrl.Length..];
        Assert.DoesNotContain("=", token);
        Assert.Equal(32, Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(token).Length);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)), row.ResetToken);
        Assert.Equal(("ACTIVE", id.ToString()), (row.RowStatus, row.CreatedBy));
        Assert.InRange(row.CreatedAt, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(row.CreatedAt.AddMinutes(30), row.ExpiredAt, TimeSpan.FromMilliseconds(1));
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        Assert.Equal("ACTIVE", (await db.UserPasswords.SingleAsync(p => p.UserId == id)).RowStatus);
        Assert.Equal(0, await db.AccessTokens.CountAsync(t => t.UserId == id));
    }

    [Theory]
    [InlineData(null, "Email Address is required and must be a valid email address.")]
    [InlineData("", "Email Address is required and must be a valid email address.")]
    [InlineData("abc", "Email Address is required and must be a valid email address.")]
    [InlineData("a@b@c", "Email Address is required and must be a valid email address.")]
    public async Task TC_RESET_002_an_invalid_email_address_is_400(string? email, string message)
    {
        await Status(await Send(email is null ? new { } : new { emailAddress = email }), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_RESET_002_a_too_long_email_address_is_400()
    {
        var email = new string('a', 255 - "@example.test".Length) + "@example.test";
        await Status(await Send(new { emailAddress = email }), 400, "E10001", "Email Address must be at most 254 characters.");
        Assert.Empty(SentTo(email));
    }

    [Fact]
    public async Task TC_RESET_003_a_new_issuance_replaces_the_active_one()
    {
        var (id, email) = await UserAsync();
        var (otherId, otherEmail) = await UserAsync();
        await Generic200(await Send(new { emailAddress = otherEmail }));

        await Generic200(await Send(new { emailAddress = email }));
        await Generic200(await Send(new { emailAddress = email }));

        var rows = await Rows(id);
        Assert.Equal(["INACTIVE", "ACTIVE"], rows.Select(r => r.RowStatus));
        Assert.Equal(id.ToString(), rows[0].UpdatedBy);
        Assert.Equal(rows[1].CreatedAt, rows[0].UpdatedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal("ACTIVE", Assert.Single(await Rows(otherId)).RowStatus);
    }

    [Fact]
    public async Task TC_RESET_004_an_unknown_email_answers_the_same_and_sends_nothing()
    {
        var (id, email) = await UserAsync();
        await Generic200(await Send(new { emailAddress = "ghost." + Guid.NewGuid() + "@example.test" }));
        await Generic200(await Send(new { emailAddress = email.ToUpperInvariant() }));
        Assert.Empty(await Rows(id));
        Assert.Empty(SentTo(email.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("INACTIVE")]
    [InlineData("LOCKED")]
    [InlineData("DELETED")]
    public async Task TC_RESET_005_a_user_that_is_not_ACTIVE_gets_no_reset(string rowStatus)
    {
        var (id, email) = await UserAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = @s WHERE id = @id", ("s", rowStatus), ("id", id));
        await Generic200(await Send(new { emailAddress = email }));
        Assert.Empty(await Rows(id));
        Assert.Empty(SentTo(email));
    }

    [Fact]
    public async Task TC_RESET_006_three_per_hour_then_refused()
    {
        var (id, email) = await UserAsync();
        for (var i = 0; i < 4; i++) await Generic200(await Send(new { emailAddress = email }));

        var rows = await Rows(id);
        Assert.Equal(3, rows.Count);
        Assert.Single(rows, r => r.RowStatus == "ACTIVE");
        Assert.Equal(3, SentTo(email).Count);
    }

    private Task OldRowsAsync(Guid userId, int count, int minutesAgo) =>
        Task.WhenAll(Enumerable.Range(0, count).Select(i => ExecuteAsync(_postgres.ConnectionString,
            "INSERT INTO authen.t_user_reset_password (created_by, created_at, row_status, user_id, reset_token, expired_at) VALUES ('test', @at, 'INACTIVE', @user, @token, @at)",
            ("at", DateTime.UtcNow.AddMinutes(-minutesAgo)), ("user", userId), ("token", RandomNumberGenerator.GetBytes(32)))));

    [Fact]
    public async Task TC_RESET_007_the_rolling_window()
    {
        var (oldId, oldEmail) = await UserAsync();
        await OldRowsAsync(oldId, 3, 61);
        await Generic200(await Send(new { emailAddress = oldEmail }));
        Assert.Equal(4, (await Rows(oldId)).Count);
        Assert.Single(SentTo(oldEmail));

        var (recentId, recentEmail) = await UserAsync();
        await OldRowsAsync(recentId, 3, 59);
        await Generic200(await Send(new { emailAddress = recentEmail }));
        Assert.Equal(3, (await Rows(recentId)).Count);
        Assert.Empty(SentTo(recentEmail));
    }

    [Fact]
    public async Task TC_RESET_008_concurrent_requests_respect_the_limit()
    {
        var (id, email) = await UserAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Send(new { emailAddress = email })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var rows = await Rows(id);
        Assert.Equal(3, rows.Count);
        Assert.Single(rows, r => r.RowStatus == "ACTIVE");
        Assert.Equal(3, SentTo(email).Count);
    }

    [Fact]
    public async Task TC_RESET_009_the_user_row_lock_makes_a_slow_request_time_out_without_writes()
    {
        var (id, email) = await UserAsync();
        await using (var blocker = new NpgsqlConnection(_postgres.ConnectionString))
        {
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var lockRow = new NpgsqlCommand("SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", blocker, transaction))
            {
                lockRow.Parameters.AddWithValue("id", id);
                await lockRow.ExecuteScalarAsync();
            }

            var client = _factory.WithWebHostBuilder(b => b.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();
            await Status(await Send(new { emailAddress = email }, client), 408, "E90001", "The server timed out waiting for the request.");
            Assert.Empty(await Rows(id));
            Assert.Empty(SentTo(email));
            await transaction.RollbackAsync();
        }

        await Generic200(await Send(new { emailAddress = email }));
        Assert.Single(await Rows(id));
    }

    private sealed class FailingResets(IPasswordResetRequestRepository inner) : IPasswordResetRequestRepository
    {
        public Task<Guid?> FindActiveUserIdAsync(string e, CancellationToken ct) => inner.FindActiveUserIdAsync(e, ct);
        public Task<bool> LockActiveUserAsync(Guid u, string e, CancellationToken ct) => inner.LockActiveUserAsync(u, e, ct);
        public Task<int> CountRequestsAsync(Guid u, DateTime w, CancellationToken ct) => inner.CountRequestsAsync(u, w, ct);
        public async Task<Guid> ReplaceActiveResetAsync(Guid u, byte[] h, DateTime n, DateTime x, CancellationToken ct)
        {
            await inner.ReplaceActiveResetAsync(u, h, n, x, ct);
            throw new InvalidOperationException("injected after the insert");
        }
    }

    [Fact]
    public async Task TC_RESET_010_an_unexpected_failure_is_500_rolls_back_and_sends_nothing()
    {
        var (id, email) = await UserAsync();
        await Generic200(await Send(new { emailAddress = email }));
        var client = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<PasswordResetRequestRepository>();
            s.AddScoped<IPasswordResetRequestRepository>(p => new FailingResets(p.GetRequiredService<PasswordResetRequestRepository>()));
        })).CreateClient();

        await Status(await Send(new { emailAddress = email }, client), 500, null, "An unexpected error occurred.");

        Assert.Equal("ACTIVE", Assert.Single(await Rows(id)).RowStatus);
        Assert.Single(SentTo(email));
    }

    [Fact]
    public async Task TC_RESET_012_the_eleventh_request_in_a_minute_is_429()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:StrictPerMinute", "10")).CreateClient();
        for (var i = 0; i < 10; i++) await Send(new { emailAddress = "x@example.test" }, client);

        var response = await Send(new { emailAddress = "x@example.test" }, client);

        await Status(response, 429, "E90002", "Too many requests. Try again later.");
        Assert.InRange(int.Parse(response.Headers.GetValues("Retry-After").Single()), 1, 60);
    }
}
