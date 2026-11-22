using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/authen-and-author/07-upsert-user-profile/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertUserProfileApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/user/profile/upsert";
    private const string Key = "unit-test-signing-key-0123456789-abcdef";
    private const string Invalid = "Invalid or expired access token.";
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FieldProtector _protector = new(FieldKey);

    public UpsertUserProfileApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", Key)
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey)));
    }

    /// <summary>A JWT shaped like TokenService's (JsonWebTokenHandler); a null <paramref name="roles"/>, <paramref name="permissions"/> or <paramref name="scopes"/> omits that claim.</summary>
    internal static string Jwt(string sub, string key = Key, int expiresInMinutes = 15, object? roles = null, object? permissions = null, object? scopes = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = sub, ["jti"] = Guid.NewGuid().ToString() };
        if (roles is not null) claims["roles"] = roles;
        if (permissions is not null) claims["permissions"] = permissions;
        if (scopes is not null) claims["scopes"] = scopes;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "ecommerce",
            Audience = "ecommerce-api",
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(expiresInMinutes),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
        });
    }

    /// <summary>A user (with an information row when <paramref name="withInformation"/>) and a stored ACTIVE access token.</summary>
    private async Task<(Guid UserId, string Jwt)> CallerAsync(bool withInformation = true)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var userId = await new RegistrationRepository(db, _protector).AddAsync(Registration(full: true), CancellationToken.None);
        if (!withInformation) await ExecuteAsync(_postgres.ConnectionString, "DELETE FROM authen.m_user_information WHERE user_id = @u", ("u", userId));
        var jwt = Jwt(userId.ToString());
        await ExecuteAsync(_postgres.ConnectionString,
            "INSERT INTO authen.t_user_access_token (created_by, user_id, session_id, token, expired_at) VALUES ('test', @u, @s, @t, @x)",
            ("u", userId), ("s", Guid.NewGuid()), ("t", SHA256.HashData(Encoding.UTF8.GetBytes(jwt))), ("x", DateTime.UtcNow.AddMinutes(15)));
        return (userId, jwt);
    }

    private Task<HttpResponseMessage> Send(string? jwt, string body, string? timestamp = "now", HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (timestamp is not null) request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        return (client ?? _factory.CreateClient()).SendAsync(request);
    }

    private static async Task<JsonElement> Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement.Clone();
        Assert.True(code == (int)response.StatusCode, $"{(int)response.StatusCode}: {root}");
        var status = root.GetProperty("status");
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        if (code != 200) Assert.False(root.TryGetProperty("data", out _));
        return root;
    }

    private async Task<(UserRecord User, UserInformationRecord? Information)> Rows(Guid userId)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return (await db.Users.SingleAsync(u => u.Id == userId), await db.UserInformation.SingleOrDefaultAsync(i => i.UserId == userId && i.RowStatus != "DELETED"));
    }

    private const string Full = """
        {"firstName":"Jane","lastName":"Roe","displayName":"Jane Roe","profilePicturePath":"/p.png","phoneNumber":"+66812345678","address":"1 Road","dateOfBirth":"1990-01-31","gender":"FEMALE"}
        """;

    [Fact]
    public async Task TC_PROFILE_001_update_both_tables()
    {
        var (userId, jwt) = await CallerAsync();

        var root = await Status(await Send(jwt, Full), 200, "S10000", "Upsert User Profile Success");

        Assert.Equal("Ok", root.GetProperty("status").GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Single(data.EnumerateObject());
        Assert.Equal(userId, data.GetProperty("userId").GetGuid());
        var (user, information) = await Rows(userId);
        Assert.Equal(("Jane", "Roe", "Jane Roe", "/p.png", userId.ToString()),
            (_protector.Unprotect(user.FirstName!), _protector.Unprotect(user.LastName!), user.DisplayName, user.ProfilePicturePath, user.UpdatedBy));
        Assert.Equal(("+66812345678", "1 Road", new DateOnly(1990, 1, 31), "FEMALE", userId.ToString()),
            (_protector.Unprotect(information!.PhoneNumber!), _protector.Unprotect(information.Address!), information.DateOfBirth!.Value, information.Gender, information.UpdatedBy));
    }

    [Fact]
    public async Task TC_PROFILE_002_first_information_row_even_next_to_a_deleted_one()
    {
        var (userId, jwt) = await CallerAsync(withInformation: false);
        await Status(await Send(jwt, """{"gender":"OTHER"}"""), 200, "S10000", null);
        var info = (await Rows(userId)).Information!;
        Assert.Equal(("OTHER", (byte[]?)null, (DateOnly?)null, userId.ToString()), (info.Gender, info.PhoneNumber, info.DateOfBirth, info.CreatedBy));

        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user_information SET row_status = 'DELETED' WHERE user_id = @u", ("u", userId));
        await Status(await Send(jwt, """{"address":"2 Road"}"""), 200, "S10000", null);
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        Assert.Equal(2, await db.UserInformation.CountAsync(i => i.UserId == userId));
        Assert.Null((await Rows(userId)).Information!.Gender);
    }

    [Fact]
    public async Task TC_PROFILE_003_omit_keeps_null_clears()
    {
        var (userId, jwt) = await CallerAsync();
        await Status(await Send(jwt, Full), 200, "S10000", null);

        await Status(await Send(jwt, """{"firstName":null,"phoneNumber":null}"""), 200, "S10000", null);

        var (user, information) = await Rows(userId);
        Assert.Null(user.FirstName);
        Assert.Null(information!.PhoneNumber);
        Assert.Equal(("Roe", "Jane Roe", "1 Road", "FEMALE"), (_protector.Unprotect(user.LastName!), user.DisplayName, _protector.Unprotect(information.Address!), information.Gender));
    }

    [Fact]
    public async Task TC_PROFILE_004_an_empty_body_writes_nothing()
    {
        var (userId, jwt) = await CallerAsync(withInformation: false);
        var root = await Status(await Send(jwt, "{}"), 200, "S10000", null);
        Assert.Equal(userId, root.GetProperty("data").GetProperty("userId").GetGuid());
        var (user, information) = await Rows(userId);
        Assert.Null(user.UpdatedAt);
        Assert.Null(information);
    }

    [Fact]
    public async Task TC_PROFILE_005_one_group_only()
    {
        var (newId, newJwt) = await CallerAsync(withInformation: false);
        await Status(await Send(newJwt, """{"displayName":"Jo"}"""), 200, "S10000", null);
        var (newUser, newInformation) = await Rows(newId);
        Assert.Null(newInformation);
        // Every user field that was not sent keeps its value.
        Assert.Equal(("Jo", "John", "Doe", "/profiles/a.png"),
            (newUser.DisplayName, _protector.Unprotect(newUser.FirstName!), _protector.Unprotect(newUser.LastName!), newUser.ProfilePicturePath));

        var (userId, jwt) = await CallerAsync();
        await Status(await Send(jwt, """{"address":"2 Road"}"""), 200, "S10000", null);
        var (user, information) = await Rows(userId);
        Assert.Null(user.UpdatedAt);
        Assert.Equal(("2 Road", "0812345678", new DateOnly(1990, 1, 31), "MALE"),
            (_protector.Unprotect(information!.Address!), _protector.Unprotect(information.PhoneNumber!), information.DateOfBirth!.Value, information.Gender));
    }

    [Theory]
    [InlineData("""{"firstName":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""", "First Name must be at most 100 characters.")]
    [InlineData("""{"phoneNumber":"1234567"}""", "Phone Number must be 8 to 15 digits with an optional leading +.")]
    [InlineData("""{"phoneNumber":"+1234567890123456"}""", "Phone Number must be 8 to 15 digits with an optional leading +.")]
    [InlineData("""{"phoneNumber":"08-1234"}""", "Phone Number must be 8 to 15 digits with an optional leading +.")]
    [InlineData("""{"dateOfBirth":"1990-02-30"}""", "Date of birth must be a real date on or before today's UTC date (YYYY-MM-DD).")]
    [InlineData("""{"dateOfBirth":"31-01-1990"}""", "Date of birth must be a real date on or before today's UTC date (YYYY-MM-DD).")]
    [InlineData("""{"gender":"male"}""", "Gender must be MALE, FEMALE or OTHER.")]
    [InlineData("""{"firstName":123}""", "First Name must be a string.")]
    public async Task TC_PROFILE_006_field_validation(string body, string message)
    {
        var (userId, jwt) = await CallerAsync();
        await Status(await Send(jwt, body), 400, "E10001", message);
        Assert.Null((await Rows(userId)).User.UpdatedAt);
    }

    [Fact]
    public async Task TC_PROFILE_006_limits_and_nulls_are_accepted()
    {
        var (_, jwt) = await CallerAsync();
        var tomorrow = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); // the host culture may use another calendar
        await Status(await Send(jwt, $$"""{"dateOfBirth":"{{tomorrow}}"}"""), 400, "E10001", "Date of birth must be a real date on or before today's UTC date (YYYY-MM-DD).");
        await Status(await Send(jwt, """{"displayName":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}"""), 400, "E10001", "Display Name must be at most 128 characters.");
        await Status(await Send(jwt, """{"firstName":null,"lastName":null,"displayName":null,"profilePicturePath":null,"phoneNumber":null,"address":null,"dateOfBirth":null,"gender":null}"""), 200, "S10000", null);
        await Status(await Send(jwt, $$"""{"firstName":"{{new string('a', 100)}}","phoneNumber":"12345678","dateOfBirth":"{{DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}}"}"""), 200, "S10000", null);
        await Status(await Send(jwt, """{"phoneNumber":"+123456789012345"}"""), 200, "S10000", null);
    }

    [Theory]
    [InlineData("""{"firstName":"A","username":"x"}""")]
    [InlineData("""{"firstName":"A","emailAddress":null}""")]
    [InlineData("""{"firstName":"A","password":"Passw0rd"}""")]
    public async Task TC_PROFILE_007_fixed_fields(string body)
    {
        var (userId, jwt) = await CallerAsync();
        await Status(await Send(jwt, body), 400, "E10001", "Username, Email Address and Password cannot be changed here.");
        Assert.Null((await Rows(userId)).User.UpdatedAt);
    }

    [Theory]
    [InlineData("""{"userId":"8d7c6b5a-4f3e-2d1c-0b9a-876543210fed"}""", "User Id is not a known field.")]
    [InlineData("""{"id":"x"}""", "Id is not a known field.")]
    [InlineData("""{"nickname":"x"}""", "Nickname is not a known field.")]
    [InlineData("[]", "Request Body must be an object.")]
    [InlineData("\"text\"", "Request Body must be an object.")]
    public async Task TC_PROFILE_008_unknown_keys_and_shape(string body, string message)
    {
        var (userId, jwt) = await CallerAsync();
        await Status(await Send(jwt, body), 400, "E10001", message);
        Assert.Null((await Rows(userId)).User.UpdatedAt);
    }

    [Fact]
    public async Task TC_PROFILE_009_validation_before_the_credential()
    {
        await Status(await Send(null, """{"gender":"male"}"""), 400, "E10001", "Gender must be MALE, FEMALE or OTHER.");
        await Status(await Send(null, "{}", timestamp: null), 400, "E10001", "Timestamp is a required header.");
    }

    [Fact]
    public async Task TC_PROFILE_010_credential_failures()
    {
        var (userId, jwt) = await CallerAsync();
        await Status(await Send(null, """{"firstName":"X"}"""), 401, "E10002", Invalid);
        await Status(await Send(Jwt(userId.ToString(), "another-signing-key-0123456789-abcdef"), """{"firstName":"X"}"""), 401, "E10002", Invalid);
        await Status(await Send(Jwt(userId.ToString()), """{"firstName":"X"}"""), 401, "E10002", Invalid); // no stored row
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", userId));
        await Status(await Send(jwt, """{"firstName":"X"}"""), 401, "E10002", Invalid);
        Assert.Null((await Rows(userId)).User.UpdatedAt);

        var (lockedId, lockedJwt) = await CallerAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", lockedId));
        await Status(await Send(lockedJwt, """{"firstName":"X"}"""), 401, "E10002", Invalid);
        Assert.Null((await Rows(lockedId)).User.UpdatedAt);
    }

    [Fact]
    public async Task TC_PROFILE_011_the_user_row_lock_makes_a_slow_request_time_out()
    {
        var (userId, jwt) = await CallerAsync();
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
            await Status(await Send(jwt, """{"firstName":"Lock"}""", client: client), 408, "E90001", "The server timed out waiting for the request.");
            await transaction.RollbackAsync();
        }

        Assert.Equal("John", _protector.Unprotect((await Rows(userId)).User.FirstName!));
        await Status(await Send(jwt, """{"firstName":"Lock"}"""), 200, "S10000", null);
    }

    private sealed class FailingProfiles(IUserProfileRepository inner) : IUserProfileRepository
    {
        public Task<bool> UpdateUserAsync(Guid u, ProfileUserChanges c, DateTime n, CancellationToken ct) => inner.UpdateUserAsync(u, c, n, ct);
        public Task<Guid?> FindInformationIdAsync(Guid u, CancellationToken ct) => throw new InvalidOperationException("injected after the user update");
        public Task<Guid> InsertInformationAsync(Guid u, ProfileInformationChanges c, CancellationToken ct) => inner.InsertInformationAsync(u, c, ct);
        public Task<bool> UpdateInformationAsync(Guid i, Guid u, ProfileInformationChanges c, DateTime n, CancellationToken ct) => inner.UpdateInformationAsync(i, u, c, n, ct);
    }

    [Fact]
    public async Task TC_PROFILE_012_an_unexpected_failure_is_500_and_rolls_back()
    {
        var (userId, jwt) = await CallerAsync();
        var client = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<UserProfileRepository>();
            s.AddScoped<IUserProfileRepository>(p => new FailingProfiles(p.GetRequiredService<UserProfileRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Full, client: client), 500, null, "An unexpected error occurred.");

        var (user, _) = await Rows(userId);
        Assert.Null(user.UpdatedAt);
        Assert.Equal("John", _protector.Unprotect(user.FirstName!));
    }
}
