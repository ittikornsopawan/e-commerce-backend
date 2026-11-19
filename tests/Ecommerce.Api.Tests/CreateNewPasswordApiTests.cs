using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/authen-and-author/04-create-new-password/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class CreateNewPasswordApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/auth/create-new-password";
    private const string OldPassword = "Passw0rd-Example";
    private const string Invalid = "Reset token is invalid or expired.";
    private const string Policy = "Password must be 8 to 128 characters and contain an upper-case letter, a lower-case letter and a digit.";
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FieldProtector _protector = new(FieldKey);
    private readonly Pbkdf2PasswordHasher _hasher = new();

    public CreateNewPasswordApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey))
            .UseSetting("RateLimit:StrictPerMinute", "10000"));
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>U_OK with its password, a reset token T_OK and tokens in two sessions (one expired but ACTIVE) plus an INACTIVE one.</summary>
    private async Task<(Guid UserId, string Token)> UserWithResetAsync(int expiresInMinutes = 30)
    {
        var registration = Registration() with { Password = _hasher.Hash(OldPassword) };
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var userId = await new RegistrationRepository(db, _protector).AddAsync(registration, CancellationToken.None);
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await ExecuteAsync(_postgres.ConnectionString,
            "INSERT INTO authen.t_user_reset_password (created_by, user_id, reset_token, expired_at) VALUES ('test', @u, @t, @x)",
            ("u", userId), ("t", Hash(token)), ("x", DateTime.UtcNow.AddMinutes(expiresInMinutes)));
        foreach (var (status, minutes) in new[] { ("ACTIVE", 15), ("ACTIVE", -5), ("INACTIVE", 15) })
            foreach (var table in new[] { "authen.t_user_access_token", "authen.t_user_refresh_token" })
            {
                await ExecuteAsync(_postgres.ConnectionString,
                    $"INSERT INTO {table} (created_by, row_status, user_id, session_id, token, expired_at) VALUES ('test', @s, @u, @sid, @t, @x)",
                    ("s", status), ("u", userId), ("sid", Guid.NewGuid()), ("t", RandomNumberGenerator.GetBytes(32)), ("x", DateTime.UtcNow.AddMinutes(minutes)));
            }

        return (userId, token);
    }

    private Task<HttpResponseMessage> Send(object body, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        return (client ?? _factory.CreateClient()).SendAsync(request);
    }

    private static async Task<(JsonElement Root, string Body)> Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement.Clone();
        Assert.Equal(code, (int)response.StatusCode);
        var status = root.GetProperty("status");
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        Assert.False(root.TryGetProperty("data", out _));
        return (root, body);
    }

    private sealed record State(string ResetStatus, int ActivePasswords, int InactivePasswords, int ActiveTokens, byte[] ActiveHash, byte[] ActiveSalt);

    private async Task<State> StateOf(Guid userId)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var reset = await db.PasswordResets.Where(r => r.UserId == userId).Select(r => r.RowStatus).SingleAsync();
        var passwords = await db.UserPasswords.Where(p => p.UserId == userId).ToListAsync();
        var active = passwords.Single(p => p.RowStatus == "ACTIVE");
        var tokens = await db.AccessTokens.CountAsync(t => t.UserId == userId && t.RowStatus == "ACTIVE")
                     + await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RowStatus == "ACTIVE");
        return new State(reset, passwords.Count(p => p.RowStatus == "ACTIVE"), passwords.Count(p => p.RowStatus == "INACTIVE"), tokens, active.Password, active.PasswordSalt);
    }

    private async Task Unchanged(Guid userId)
    {
        var state = await StateOf(userId);
        Assert.Equal(("ACTIVE", 1, 0, 4), (state.ResetStatus, state.ActivePasswords, state.InactivePasswords, state.ActiveTokens));
        Assert.True(_hasher.Verify(OldPassword, new Ecommerce.Domain.Authen.ValueObjects.PasswordCredential(state.ActiveHash, state.ActiveSalt)));
    }

    [Fact]
    public async Task TC_NEWPW_001_the_password_is_replaced_and_every_session_is_closed()
    {
        var (userId, token) = await UserWithResetAsync();
        var (otherId, _) = await UserWithResetAsync();

        var (root, body) = await Status(await Send(new { resetToken = token, newPassword = "NewSecret1x" }), 200, "S10000", "Create New Password Success");

        Assert.Equal("Ok", root.GetProperty("status").GetProperty("message").GetString());
        Assert.DoesNotContain(token, body);
        Assert.DoesNotContain("NewSecret1x", body);
        var state = await StateOf(userId);
        Assert.Equal(("INACTIVE", 1, 1, 0), (state.ResetStatus, state.ActivePasswords, state.InactivePasswords, state.ActiveTokens));
        Assert.Equal(16, state.ActiveSalt.Length);
        var credential = new Ecommerce.Domain.Authen.ValueObjects.PasswordCredential(state.ActiveHash, state.ActiveSalt);
        Assert.True(_hasher.Verify("NewSecret1x", credential));
        Assert.False(_hasher.Verify(OldPassword, credential));
        await Unchanged(otherId);
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        Assert.Equal(3, await db.AccessTokens.CountAsync(t => t.UserId == userId));
        Assert.Equal(userId.ToString(), (await db.PasswordResets.SingleAsync(r => r.UserId == userId)).UpdatedBy);
    }

    [Fact]
    public async Task TC_NEWPW_002_missing_fields()
    {
        var (userId, token) = await UserWithResetAsync();
        await Status(await Send(new { newPassword = "NewSecret1x" }), 400, "E10001", "Reset Token is a required field.");
        await Status(await Send(new { resetToken = token }), 400, "E10001", "New Password is a required field.");
        await Status(await Send(new { }), 400, "E10001", "Reset Token and New Password are required fields.");
        await Unchanged(userId);
    }

    [Theory]
    [InlineData("Short1")]
    [InlineData("nouppercase1")]
    [InlineData("NOLOWERCASE1")]
    [InlineData("NoDigitsHere")]
    public async Task TC_NEWPW_003_the_password_policy_answers_before_any_lookup(string password)
    {
        var (userId, token) = await UserWithResetAsync();
        await Status(await Send(new { resetToken = token, newPassword = password }), 400, "E10001", Policy);
        await Status(await Send(new { resetToken = "unknown", newPassword = password }), 400, "E10001", Policy);
        await Unchanged(userId);
    }

    [Fact]
    public async Task TC_NEWPW_003_the_policy_upper_bound()
    {
        var (userId, token) = await UserWithResetAsync();
        await Status(await Send(new { resetToken = token, newPassword = "Aa1" + new string('x', 126) }), 400, "E10001", Policy);
        await Unchanged(userId);
    }

    [Fact]
    public async Task TC_NEWPW_004_an_unknown_token()
    {
        var (userId, _) = await UserWithResetAsync();
        await Status(await Send(new { resetToken = "unknown-token", newPassword = "NewSecret1x" }), 400, "E10001", Invalid);
        await Unchanged(userId);
    }

    [Fact]
    public async Task TC_NEWPW_005_a_used_or_replaced_token()
    {
        var (userId, token) = await UserWithResetAsync();
        await Status(await Send(new { resetToken = token, newPassword = "NewSecret1x" }), 200, "S10000", null);
        await Status(await Send(new { resetToken = token, newPassword = "Another1x" }), 400, "E10001", Invalid);
        Assert.Equal(1, (await StateOf(userId)).InactivePasswords);

        var (replacedUser, replaced) = await UserWithResetAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_reset_password SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", replacedUser));
        await Status(await Send(new { resetToken = replaced, newPassword = "NewSecret1x" }), 400, "E10001", Invalid);
    }

    [Fact]
    public async Task TC_NEWPW_006_an_expired_token()
    {
        var (userId, token) = await UserWithResetAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_reset_password SET expired_at = @x WHERE user_id = @u",
            ("x", DateTime.UtcNow.AddSeconds(-1)), ("u", userId));
        await Status(await Send(new { resetToken = token, newPassword = "NewSecret1x" }), 400, "E10001", Invalid);
        await Unchanged(userId);
    }

    [Theory]
    [InlineData("LOCKED")]
    [InlineData("INACTIVE")]
    public async Task TC_NEWPW_007_a_user_that_is_not_active(string rowStatus)
    {
        var (userId, token) = await UserWithResetAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = @s WHERE id = @id", ("s", rowStatus), ("id", userId));
        await Status(await Send(new { resetToken = token, newPassword = "NewSecret1x" }), 400, "E10001", Invalid);
        await Unchanged(userId);
    }

    [Fact]
    public async Task TC_NEWPW_008_the_same_password_is_refused_and_the_token_stays_usable()
    {
        var (userId, token) = await UserWithResetAsync();
        await Status(await Send(new { resetToken = token, newPassword = OldPassword }), 400, "E10001", "New password must be different from the current password.");
        await Unchanged(userId);
        await Status(await Send(new { resetToken = token, newPassword = "NewSecret1x" }), 200, "S10000", "Create New Password Success");
    }

    [Fact]
    public async Task TC_NEWPW_009_concurrent_requests_consume_the_token_once()
    {
        var (userId, token) = await UserWithResetAsync();

        var responses = await Task.WhenAll(new[] { "NewSecret1x", "NewSecret2x", "NewSecret3x" }
            .Select(p => Send(new { resetToken = token, newPassword = p })));

        Assert.Equal([200, 400, 400], responses.Select(r => (int)r.StatusCode).Order());
        var state = await StateOf(userId);
        Assert.Equal((1, 1), (state.ActivePasswords, state.InactivePasswords));
    }

    [Fact]
    public async Task TC_NEWPW_010_the_user_row_lock_makes_a_slow_request_time_out_without_changes()
    {
        var (userId, token) = await UserWithResetAsync();
        await using (var blocker = new NpgsqlConnection(_postgres.ConnectionString))
        {
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var lockRow = new NpgsqlCommand("SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", blocker, transaction))
            {
                lockRow.Parameters.AddWithValue("id", userId);
                await lockRow.ExecuteScalarAsync();
            }

            var client = _factory.WithWebHostBuilder(b => b.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();
            await Status(await Send(new { resetToken = token, newPassword = "NewSecret1x" }, client), 408, "E90001", "The server timed out waiting for the request.");
            await Unchanged(userId);
            await transaction.RollbackAsync();
        }

        await Status(await Send(new { resetToken = token, newPassword = "NewSecret1x" }), 200, "S10000", null);
    }

    [Fact]
    public async Task TC_NEWPW_011_a_missing_current_password_is_500_and_rolls_back()
    {
        var (userId, token) = await UserWithResetAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user_password SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", userId));

        await Status(await Send(new { resetToken = token, newPassword = "NewSecret1x" }), 500, null, "An unexpected error occurred.");

        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        Assert.Equal("ACTIVE", (await db.PasswordResets.SingleAsync(r => r.UserId == userId)).RowStatus);
        Assert.Equal(4, await db.AccessTokens.CountAsync(t => t.UserId == userId && t.RowStatus == "ACTIVE")
                        + await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RowStatus == "ACTIVE"));
    }

    [Fact]
    public async Task TC_NEWPW_012_the_eleventh_request_in_a_minute_is_429()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:StrictPerMinute", "10")).CreateClient();
        for (var i = 0; i < 10; i++) await Send(new { resetToken = "x", newPassword = "NewSecret1x" }, client);

        var response = await Send(new { resetToken = "x", newPassword = "NewSecret1x" }, client);

        await Status(response, 429, "E90002", "Too many requests. Try again later.");
        Assert.InRange(int.Parse(response.Headers.GetValues("Retry-After").Single()), 1, 60);
    }
}
