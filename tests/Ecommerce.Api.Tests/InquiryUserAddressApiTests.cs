using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/authen-and-author/10-inquiry-user-address/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class InquiryUserAddressApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/user/address";
    private const string Invalid = "Invalid or expired access token.";
    private const string PageRange = "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100.";
    private const string RowStatus = "Row status must be one of ACTIVE, INACTIVE.";
    private static readonly string[] DataKeys = ["items", "pageIndex", "pageSize", "totalItems", "totalPages"];
    private static readonly string[] ItemKeys = ["userAddressId", "title", "isDefault", "address", "city", "state", "postalCode", "country", "rowStatus"];
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FieldProtector _protector = new(FieldKey);

    public InquiryUserAddressApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey)));
    }

    private sealed record Caller(Guid UserId, string Jwt, Guid Home, Guid Office, Guid OldFlat, Guid Gone);

    /// <summary>U_OK of the shared fixtures: Home (default, oldest), Office and Old flat (same created_at), Gone (DELETED).</summary>
    private async Task<Caller> CallerAsync()
    {
        var (userId, jwt) = await UserAsync();
        var t = DateTime.UtcNow.AddDays(-1);
        var home = await AddressAsync(userId, "Home", "ACTIVE", true, t.AddHours(-5));
        var office = await AddressAsync(userId, "Office", "ACTIVE", false, t);
        var oldFlat = await AddressAsync(userId, "Old flat", "INACTIVE", false, t);
        var gone = await AddressAsync(userId, "Gone", "DELETED", false, t.AddHours(1));
        return new Caller(userId, jwt, home, office, oldFlat, gone);
    }

    private async Task<(Guid UserId, string Jwt)> UserAsync()
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var userId = await new RegistrationRepository(db, _protector).AddAsync(Registration(), CancellationToken.None);
        var jwt = UpsertUserProfileApiTests.Jwt(userId.ToString());
        await ExecuteAsync(_postgres.ConnectionString,
            "INSERT INTO authen.t_user_access_token (created_by, user_id, session_id, token, expired_at) VALUES ('test', @u, @s, @t, @x)",
            ("u", userId), ("s", Guid.NewGuid()), ("t", SHA256.HashData(Encoding.UTF8.GetBytes(jwt))), ("x", DateTime.UtcNow.AddMinutes(15)));
        return (userId, jwt);
    }

    private async Task<Guid> AddressAsync(Guid userId, string title, string rowStatus, bool isDefault, DateTime createdAt, Guid? fixedId = null)
    {
        var id = fixedId ?? Guid.NewGuid();
        await ExecuteAsync(_postgres.ConnectionString,
            """
            INSERT INTO authen.t_user_address (id, created_by, created_at, row_status, user_id, title, is_default, address, city, state, postal_code, country)
            VALUES (@id, 'test', @c, @r, @u, @title, @d, @a, @city, @s, @p, @country)
            """,
            ("id", id), ("c", createdAt), ("r", rowStatus), ("u", userId), ("title", title), ("d", isDefault),
            ("a", _protector.Protect($"{title} Road")), ("city", _protector.Protect("Watthana")), ("s", _protector.Protect("Bangkok")),
            ("p", _protector.Protect("10110")), ("country", _protector.Protect("Thailand")));
        return id;
    }

    private Task<HttpResponseMessage> Send(string? jwt, string query = "", string? body = null, bool timestamp = true, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Path + query);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (timestamp) request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
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

    private async Task<JsonElement> Data(string jwt, string query = "", HttpClient? client = null) =>
        (await Status(await Send(jwt, query, client: client), 200, "S10000", "Inquiry User Address Success")).GetProperty("data");

    private static string[] Titles(JsonElement data) => data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToArray();

    private static (long Index, long Size, long Items, long Pages) Totals(JsonElement data) =>
        (data.GetProperty("pageIndex").GetInt64(), data.GetProperty("pageSize").GetInt64(), data.GetProperty("totalItems").GetInt64(), data.GetProperty("totalPages").GetInt64());

    [Fact]
    public async Task TC_ADDRESSQ_001_the_own_list_in_order()
    {
        var c = await CallerAsync();
        await AddressAsync((await UserAsync()).UserId, "Home", "ACTIVE", true, DateTime.UtcNow);

        var data = await Data(c.Jwt);

        Assert.Equal(DataKeys, data.EnumerateObject().Select(p => p.Name));
        Assert.Equal((1L, 10L, 3L, 1L), Totals(data));
        var sameTime = new[] { (c.Office, "Office"), (c.OldFlat, "Old flat") }.OrderBy(x => x.Item1.ToString()).Select(x => x.Item2);
        Assert.Equal(new[] { "Home" }.Concat(sameTime), Titles(data));

        var home = data.GetProperty("items")[0];
        Assert.Equal(ItemKeys, home.EnumerateObject().Select(p => p.Name));
        Assert.Equal((c.Home, "Home", true, "Home Road", "Watthana", "Bangkok", "10110", "Thailand", "ACTIVE"),
            (home.GetProperty("userAddressId").GetGuid(), home.GetProperty("title").GetString(), home.GetProperty("isDefault").GetBoolean(),
             home.GetProperty("address").GetString(), home.GetProperty("city").GetString(), home.GetProperty("state").GetString(),
             home.GetProperty("postalCode").GetString(), home.GetProperty("country").GetString(), home.GetProperty("rowStatus").GetString()));

        // The newest row has the largest id, so only created_at DESC (not the id tie-break) puts it right after the default.
        await AddressAsync(c.UserId, "Newest", "INACTIVE", false, DateTime.UtcNow, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        Assert.Equal(new[] { "Home", "Newest" }.Concat(sameTime), Titles(await Data(c.Jwt)));
    }

    [Fact]
    public async Task TC_ADDRESSQ_002_by_id()
    {
        var c = await CallerAsync();
        var otherId = await AddressAsync((await UserAsync()).UserId, "Home", "ACTIVE", true, DateTime.UtcNow);

        var one = await Data(c.Jwt, $"?id={c.Office}");
        Assert.Equal(["Office"], Titles(one));
        Assert.Equal((1L, 10L, 1L, 1L), Totals(one));

        foreach (var query in new[] { $"?id={otherId}", $"?id={c.Gone}", $"?id={Guid.NewGuid()}", $"?id={c.Office}&search=home", $"?id={c.Office}&rowStatus=INACTIVE" })
        {
            var empty = await Data(c.Jwt, query);
            Assert.Empty(Titles(empty));
            Assert.Equal((1L, 10L, 0L, 0L), Totals(empty));
        }

        var beyond = await Data(c.Jwt, $"?id={c.Office}&pageIndex=2");
        Assert.Empty(Titles(beyond));
        Assert.Equal((2L, 10L, 1L, 1L), Totals(beyond));
    }

    [Fact]
    public async Task TC_ADDRESSQ_003_status_visibility()
    {
        var c = await CallerAsync();
        Assert.Equal(3, Titles(await Data(c.Jwt)).Length);
        Assert.Equal(["Home", "Office"], Titles(await Data(c.Jwt, "?rowStatus=ACTIVE")));
        Assert.Equal(["Old flat"], Titles(await Data(c.Jwt, "?rowStatus=INACTIVE")));
    }

    [Fact]
    public async Task TC_ADDRESSQ_004_search_by_title()
    {
        var c = await CallerAsync();
        Assert.Equal(["Office"], Titles(await Data(c.Jwt, "?search=OFF")));
        Assert.Equal(3, Titles(await Data(c.Jwt, "?search=")).Length);
        Assert.Equal(["Old flat"], Titles(await Data(c.Jwt, "?search=o_d")));
        Assert.Equal(3, Titles(await Data(c.Jwt, "?search=%25")).Length);
        Assert.Empty(Titles(await Data(c.Jwt, "?search=Road")));
    }

    [Fact]
    public async Task TC_ADDRESSQ_005_pagination_and_totals()
    {
        var c = await CallerAsync();
        var all = Titles(await Data(c.Jwt));

        var first = await Data(c.Jwt, "?pageSize=2");
        Assert.Equal(all[..2], Titles(first));
        Assert.Equal((1L, 2L, 3L, 2L), Totals(first));
        Assert.Equal(all[2..], Titles(await Data(c.Jwt, "?pageIndex=2&pageSize=2")));

        var beyond = await Data(c.Jwt, "?pageIndex=3&pageSize=2");
        Assert.Empty(Titles(beyond));
        Assert.Equal((3L, 2L, 3L, 2L), Totals(beyond));
        Assert.Equal((1L, 100L, 3L, 1L), Totals(await Data(c.Jwt, "?pageSize=100")));

        var (_, emptyJwt) = await UserAsync();
        var none = await Data(emptyJwt);
        Assert.Equal(JsonValueKind.Array, none.GetProperty("items").ValueKind);
        Assert.Empty(Titles(none));
        Assert.Equal((1L, 10L, 0L, 0L), Totals(none));
    }

    [Theory]
    [InlineData("?pageIndex=0", PageRange)]
    [InlineData("?pageIndex=-1", PageRange)]
    [InlineData("?pageIndex=abc", PageRange)]
    [InlineData("?pageIndex=", PageRange)]
    [InlineData("?pageIndex=1.5", PageRange)]
    [InlineData("?pageIndex=%2B1", PageRange)]
    [InlineData("?pageIndex=9223372036854775808", PageRange)]
    [InlineData("?pageSize=0", PageRange)]
    [InlineData("?pageSize=101", PageRange)]
    [InlineData("?pageSize=", PageRange)]
    [InlineData("?pageIndex=9223372036854775807&pageSize=2", "Page Index is too large for Page Size.")]
    [InlineData("?rowStatus=DELETED", RowStatus)]
    [InlineData("?rowStatus=active", RowStatus)]
    [InlineData("?rowStatus=", RowStatus)]
    [InlineData("?id=abc", "Id must be a valid UUID.")]
    [InlineData("?id=", "Id must be a valid UUID.")]
    [InlineData("?id=abc&rowStatus=DELETED", "Id must be a valid UUID, Row status must be one of ACTIVE, INACTIVE.")]
    public async Task TC_ADDRESSQ_006_query_validation(string query, string message)
    {
        var c = await UserAsync();
        await Status(await Send(c.Jwt, query), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_ADDRESSQ_007_request_shape_before_the_credential()
    {
        var (otherId, jwt) = await UserAsync();
        foreach (var token in new[] { jwt, null })
        {
            await Status(await Send(token, $"?userId={otherId}"), 400, "E10001", "User Id is not a known field.");
            await Status(await Send(token, "?pageIndex=1&pageIndex=2"), 400, "E10001", "Page Index must not be duplicated.");
            await Status(await Send(token, body: "{}"), 400, "E10001", "Request Body must be empty.");
        }

        await Status(await Send(null, "?pageSize=0"), 400, "E10001", PageRange);
        await Status(await Send(jwt, timestamp: false), 400, "E10001", "Timestamp is a required header.");
    }

    [Fact]
    public async Task TC_ADDRESSQ_008_credential_failures()
    {
        var (userId, jwt) = await UserAsync();
        await Status(await Send(null), 401, "E10002", Invalid);
        await Status(await Send(UpsertUserProfileApiTests.Jwt(userId.ToString(), "another-signing-key-0123456789-abcdef")), 401, "E10002", Invalid);
        await Status(await Send(UpsertUserProfileApiTests.Jwt(userId.ToString())), 401, "E10002", Invalid);
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_access_token SET expired_at = @x WHERE user_id = @u", ("x", DateTime.UtcNow.AddSeconds(-1)), ("u", userId));
        await Status(await Send(jwt), 401, "E10002", Invalid);
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_access_token SET expired_at = @x, row_status = 'INACTIVE' WHERE user_id = @u", ("x", DateTime.UtcNow.AddMinutes(5)), ("u", userId));
        await Status(await Send(jwt), 401, "E10002", Invalid);

        var (lockedId, lockedJwt) = await UserAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", lockedId));
        await Status(await Send(lockedJwt), 401, "E10002", Invalid);
    }

    private async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> HoldAsync(string sql, Guid userId)
    {
        var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("u", userId);
        await command.ExecuteNonQueryAsync();
        return (connection, transaction);
    }

    private const string Unchanged = "(SELECT * FROM authen.t_user_address WHERE updated_at IS NULL) a";

    private HttpClient OneSecondClient() => _factory.WithWebHostBuilder(b => b.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

    [Fact]
    public async Task TC_ADDRESSQ_009_the_read_takes_no_lock()
    {
        var c = await CallerAsync();
        var before = await CountAsync(_postgres.ConnectionString, Unchanged, c.UserId);
        var (connection, transaction) = await HoldAsync(
            "SELECT 1 FROM authen.m_user WHERE id = @u FOR UPDATE; SELECT 1 FROM authen.t_user_address WHERE user_id = @u FOR UPDATE", c.UserId);

        Assert.Equal(3, Titles(await Data(c.Jwt, client: OneSecondClient())).Length);

        await transaction.RollbackAsync();
        await connection.DisposeAsync();
        Assert.Equal(before, await CountAsync(_postgres.ConnectionString, Unchanged, c.UserId));
    }

    [Fact]
    public async Task TC_ADDRESSQ_010_timeout()
    {
        var c = await CallerAsync();
        var (connection, transaction) = await HoldAsync("LOCK TABLE authen.t_user_address IN ACCESS EXCLUSIVE MODE; SELECT @u", c.UserId);

        await Status(await Send(c.Jwt, client: OneSecondClient()), 408, "E90001", "The server timed out waiting for the request.");

        await transaction.RollbackAsync();
        await connection.DisposeAsync();
        Assert.Equal(3, Titles(await Data(c.Jwt)).Length);
    }

    /// <summary>Commits a new ACTIVE address of the user from another connection right after the count.</summary>
    private sealed class InsertAfterCount(IUserAddressQueryRepository inner, Func<Task> insert) : IUserAddressQueryRepository
    {
        public async Task<long> CountAsync(AddressFilter f, CancellationToken ct)
        {
            var total = await inner.CountAsync(f, ct);
            await insert();
            return total;
        }

        public Task<IReadOnlyList<AddressRow>> ListAsync(AddressFilter f, long o, long l, CancellationToken ct) => inner.ListAsync(f, o, l, ct);
    }

    private HttpClient ClientWith(Func<IUserAddressQueryRepository, IUserAddressQueryRepository> wrap) =>
        _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<UserAddressQueryRepository>();
            s.AddScoped<IUserAddressQueryRepository>(p => wrap(p.GetRequiredService<UserAddressQueryRepository>()));
        })).CreateClient();

    [Fact]
    public async Task TC_ADDRESSQ_011_one_snapshot_for_count_and_page()
    {
        var c = await CallerAsync();
        var inserted = false;
        var client = ClientWith(inner => new InsertAfterCount(inner, async () =>
        {
            if (inserted) return;
            inserted = true;
            await AddressAsync(c.UserId, "Late", "ACTIVE", false, DateTime.UtcNow);
        }));

        var data = await Data(c.Jwt, client: client);
        Assert.True(inserted);
        Assert.Equal(3L, Totals(data).Items);
        Assert.DoesNotContain("Late", Titles(data));
        Assert.Equal(4, Titles(await Data(c.Jwt)).Length);
    }

    private sealed class FailingPage(IUserAddressQueryRepository inner, bool throws) : IUserAddressQueryRepository
    {
        public Task<long> CountAsync(AddressFilter f, CancellationToken ct) => inner.CountAsync(f, ct);

        public async Task<IReadOnlyList<AddressRow>> ListAsync(AddressFilter f, long o, long l, CancellationToken ct) =>
            throws ? throw new InvalidOperationException("injected") : (await inner.ListAsync(f, o, l, ct)).Skip(1).ToList();
    }

    [Fact]
    public async Task TC_ADDRESSQ_012_unexpected_failure()
    {
        var c = await CallerAsync();
        foreach (var throws in new[] { true, false })
            await Status(await Send(c.Jwt, client: ClientWith(inner => new FailingPage(inner, throws))), 500, null, "An unexpected error occurred.");
    }
}
