using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/authen-and-author/09-upsert-user-address/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertUserAddressApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/user/address/upsert";
    private readonly PostgresFixture _postgres;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FieldProtector _protector = new(FieldKey);

    public UpsertUserAddressApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _factory = factory.WithWebHostBuilder(b => b
            .UseSetting("Database:ConnectionString", postgres.ConnectionString)
            .UseSetting("Jwt:SigningKey", "unit-test-signing-key-0123456789-abcdef")
            .UseSetting("Security:FieldEncryptionKey", Convert.ToBase64String(FieldKey)));
    }

    /// <summary>A user with no address and an ACTIVE stored access token.</summary>
    private async Task<(Guid UserId, string Jwt)> CallerAsync()
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        var userId = await new RegistrationRepository(db, _protector).AddAsync(Registration(), CancellationToken.None);
        var jwt = UpsertUserProfileApiTests.Jwt(userId.ToString());
        await ExecuteAsync(_postgres.ConnectionString,
            "INSERT INTO authen.t_user_access_token (created_by, user_id, session_id, token, expired_at) VALUES ('test', @u, @s, @t, @x)",
            ("u", userId), ("s", Guid.NewGuid()), ("t", SHA256.HashData(Encoding.UTF8.GetBytes(jwt))), ("x", DateTime.UtcNow.AddMinutes(15)));
        return (userId, jwt);
    }

    private static JsonObject Body(string title = "Home", Guid? id = null, string? rowStatus = null, bool? isDefault = null)
    {
        var body = new JsonObject
        {
            ["title"] = title,
            ["address"] = "1 Road",
            ["city"] = "Watthana",
            ["state"] = "Bangkok",
            ["postalCode"] = "10110",
            ["country"] = "Thailand"
        };
        if (id is not null) body["id"] = id.ToString();
        if (rowStatus is not null) body["rowStatus"] = rowStatus;
        if (isDefault is not null) body["isDefault"] = isDefault;
        return body;
    }

    private Task<HttpResponseMessage> Send(string? jwt, string body, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (jwt is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.Add("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        return (client ?? _factory.CreateClient()).SendAsync(request);
    }

    private Task<HttpResponseMessage> Send(string? jwt, JsonNode body, HttpClient? client = null) => Send(jwt, body.ToJsonString(), client);

    private static async Task<JsonElement> Status(HttpResponseMessage response, int code, string? bizCode, string? bizMessage)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement.Clone();
        Assert.True(code == (int)response.StatusCode, $"{(int)response.StatusCode}: {root}");
        var status = root.GetProperty("status");
        if (bizCode is null) Assert.False(status.TryGetProperty("bizCode", out _));
        else Assert.Equal(bizCode, status.GetProperty("bizCode").GetString());
        if (bizMessage is not null) Assert.Equal(bizMessage, status.GetProperty("bizMessage").GetString());
        if (code >= 400) Assert.False(root.TryGetProperty("data", out _));
        return root;
    }

    private async Task<Guid> InsertAsync(string jwt, JsonObject body) =>
        (await Status(await Send(jwt, body), 201, "S10000", "Insert User Address Success")).GetProperty("data").GetProperty("userAddressId").GetGuid();

    private async Task<List<UserAddressRecord>> Rows(Guid userId)
    {
        await using var db = EcommerceDbContext.Create(_postgres.ConnectionString);
        return await db.UserAddresses.Where(a => a.UserId == userId).ToListAsync();
    }

    private async Task<Guid?> DefaultOf(Guid userId) => (await Rows(userId)).SingleOrDefault(a => a.IsDefault && a.RowStatus != "DELETED")?.Id;

    [Fact]
    public async Task TC_ADDRESS_001_insert_the_first_address()
    {
        var (userId, jwt) = await CallerAsync();

        var root = await Status(await Send(jwt, Body(isDefault: false)), 201, "S10000", "Insert User Address Success");

        Assert.Equal("Created", root.GetProperty("status").GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Single(data.EnumerateObject());
        var row = Assert.Single(await Rows(userId));
        Assert.Equal(data.GetProperty("userAddressId").GetGuid(), row.Id);
        Assert.Equal(("ACTIVE", true, "Home", userId.ToString()), (row.RowStatus, row.IsDefault, row.Title, row.CreatedBy));
        Assert.Equal(("1 Road", "Watthana", "Bangkok", "10110", "Thailand"),
            (_protector.Unprotect(row.Address), _protector.Unprotect(row.City), _protector.Unprotect(row.State), _protector.Unprotect(row.PostalCode), _protector.Unprotect(row.Country)));
    }

    [Fact]
    public async Task TC_ADDRESS_002_default_rules_on_insert()
    {
        var (userId, jwt) = await CallerAsync();
        var a1 = await InsertAsync(jwt, Body("A1"));
        await InsertAsync(jwt, Body("A2"));
        Assert.Equal(a1, await DefaultOf(userId));

        var (freshId, freshJwt) = await CallerAsync();
        await InsertAsync(freshJwt, Body("B1", rowStatus: "INACTIVE"));
        Assert.Null(await DefaultOf(freshId));
        var b2 = await InsertAsync(freshJwt, Body("B2"));
        Assert.Equal(b2, await DefaultOf(freshId));
    }

    [Fact]
    public async Task TC_ADDRESS_003_choosing_a_new_default_keeps_exactly_one()
    {
        var (userId, jwt) = await CallerAsync();
        var a1 = await InsertAsync(jwt, Body("A1"));
        await InsertAsync(jwt, Body("A2"));
        var a3 = await InsertAsync(jwt, Body("A3", isDefault: true));
        Assert.Equal(a3, await DefaultOf(userId));

        await Status(await Send(jwt, Body("A1", id: a1, isDefault: false)), 200, "S10000", null);
        Assert.Equal(a3, await DefaultOf(userId));
        await Status(await Send(jwt, Body("A3 renamed", id: a3)), 200, "S10000", null);
        Assert.Equal(a3, await DefaultOf(userId));
        Assert.Single(await Rows(userId), a => a.IsDefault);
    }

    [Fact]
    public async Task TC_ADDRESS_004_update()
    {
        var (userId, jwt) = await CallerAsync();
        await InsertAsync(jwt, Body("Default"));
        var a1 = await InsertAsync(jwt, Body("A1"));

        var root = await Status(await Send(jwt, Body("Office", id: a1, rowStatus: "INACTIVE")), 200, "S10000", "Update User Address Success");

        Assert.Equal("Ok", root.GetProperty("status").GetProperty("message").GetString());
        Assert.Equal(a1, root.GetProperty("data").GetProperty("userAddressId").GetGuid());
        var row = (await Rows(userId)).Single(a => a.Id == a1);
        Assert.Equal(("Office", "INACTIVE", false, userId.ToString()), (row.Title, row.RowStatus, row.IsDefault, row.UpdatedBy));
    }

    [Fact]
    public async Task TC_ADDRESS_005_soft_delete()
    {
        var (userId, jwt) = await CallerAsync();
        await InsertAsync(jwt, Body("A1"));
        var a2 = await InsertAsync(jwt, Body("A2"));

        var response = await Send(jwt, Body("A2", id: a2, rowStatus: "DELETED", isDefault: false));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        var row = (await Rows(userId)).Single(a => a.Id == a2);
        Assert.Equal(("DELETED", false), (row.RowStatus, row.IsDefault));
        await Status(await Send(jwt, Body("A2", id: a2, rowStatus: "DELETED")), 404, "E10004", "User address not found.");
    }

    [Fact]
    public async Task TC_ADDRESS_006_promotion_after_the_default_leaves()
    {
        var (userId, jwt) = await CallerAsync();
        var a1 = await InsertAsync(jwt, Body("A1"));
        var a2 = await InsertAsync(jwt, Body("A2"));
        var a3 = await InsertAsync(jwt, Body("A3"));
        await Status(await Send(jwt, Body("A3 latest", id: a3)), 200, "S10000", null);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(jwt, Body("A1", id: a1, rowStatus: "DELETED"))).StatusCode);
        Assert.Equal(a3, await DefaultOf(userId));
        await Status(await Send(jwt, Body("A3", id: a3, rowStatus: "INACTIVE")), 200, "S10000", null);
        Assert.Equal(a2, await DefaultOf(userId));
        Assert.Equal(HttpStatusCode.NoContent, (await Send(jwt, Body("A2", id: a2, rowStatus: "DELETED"))).StatusCode);
        Assert.Null(await DefaultOf(userId));
    }

    [Fact]
    public async Task TC_ADDRESS_007_q21_a_default_must_be_active()
    {
        var (userId, jwt) = await CallerAsync();
        await Status(await Send(jwt, Body(rowStatus: "INACTIVE", isDefault: true)), 400, "E10001", "A default address must be ACTIVE.");
        await Status(await Send(jwt, Body(id: Guid.NewGuid(), rowStatus: "DELETED", isDefault: true)), 400, "E10001", "A default address must be ACTIVE.");
        Assert.Empty(await Rows(userId));
    }

    public static IEnumerable<object[]> InvalidBodies()
    {
        JsonObject Without(string field) { var b = Body(); b.Remove(field); return b; }
        JsonObject With(string field, JsonNode? value) { var b = Body(); b[field] = value; return b; }
        yield return ["""{"isDefault":false}""", "Title, Address, City, State, Postal Code and Country are required fields."];
        yield return [Without("city").ToJsonString(), "City is a required field."];
        yield return [With("postalCode", "  ").ToJsonString(), "Postal Code is a required field."];
        yield return [With("title", new string('t', 129)).ToJsonString(), "Title must be at most 128 characters."];
        yield return [With("id", "x").ToJsonString(), "Id must be a valid UUID."];
        yield return [With("id", null).ToJsonString(), "Id must be a valid UUID."];
        yield return [With("rowStatus", "ARCHIVED").ToJsonString(), "Row Status must be ACTIVE, INACTIVE or DELETED."];
        yield return [With("rowStatus", null).ToJsonString(), "Row Status must be ACTIVE, INACTIVE or DELETED."];
        yield return [With("isDefault", "yes").ToJsonString(), "Is Default must be a boolean."];
        yield return [With("isDefault", null).ToJsonString(), "Is Default must be a boolean."];
        yield return [With("userId", Guid.NewGuid().ToString()).ToJsonString(), "User Id is not a known field."];
        yield return [With("rowStatus", "DELETED").ToJsonString(), "Row status must not be DELETED when inserting a user address."];
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task TC_ADDRESS_008_validation(string body, string message)
    {
        var (userId, jwt) = await CallerAsync();
        await Status(await Send(jwt, body), 400, "E10001", message);
        Assert.Empty(await Rows(userId));
    }

    [Fact]
    public async Task TC_ADDRESS_008_a_delete_still_needs_every_mandatory_field()
    {
        var (userId, jwt) = await CallerAsync();
        var a1 = await InsertAsync(jwt, Body("A1"));
        var body = Body("A1", id: a1, rowStatus: "DELETED");
        body.Remove("country");
        await Status(await Send(jwt, body), 400, "E10001", "Country is a required field.");
        Assert.Equal("ACTIVE", (await Rows(userId)).Single().RowStatus);
    }

    [Fact]
    public async Task TC_ADDRESS_009_at_most_10_addresses()
    {
        var (userId, jwt) = await CallerAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++) ids.Add(await InsertAsync(jwt, Body($"A{i}", rowStatus: i % 2 == 0 ? "ACTIVE" : "INACTIVE")));

        await Status(await Send(jwt, Body("Eleventh")), 400, "E10001", "A user can have at most 10 addresses.");
        Assert.Equal(10, (await Rows(userId)).Count);
        await Status(await Send(jwt, Body("Renamed", id: ids[1], rowStatus: "INACTIVE")), 200, "S10000", null);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(jwt, Body("Gone", id: ids[1], rowStatus: "DELETED"))).StatusCode);
        await InsertAsync(jwt, Body("Tenth again"));
    }

    [Fact]
    public async Task TC_ADDRESS_010_not_found()
    {
        var (otherId, otherJwt) = await CallerAsync();
        var foreign = await InsertAsync(otherJwt, Body("Theirs"));
        var (userId, jwt) = await CallerAsync();
        var mine = await InsertAsync(jwt, Body("Mine"));
        Assert.Equal(HttpStatusCode.NoContent, (await Send(jwt, Body("Mine", id: mine, rowStatus: "DELETED"))).StatusCode);

        foreach (var id in new[] { foreign, mine, Guid.NewGuid() })
            await Status(await Send(jwt, Body("X", id: id)), 404, "E10004", "User address not found.");
        Assert.Equal("Theirs", (await Rows(otherId)).Single().Title);
    }

    [Fact]
    public async Task TC_ADDRESS_011_validation_before_the_credential_and_credential_failures()
    {
        var (userId, jwt) = await CallerAsync();
        await Status(await Send(null, """{"isDefault":false}"""), 400, "E10001", null);
        foreach (var token in new[] { null, UpsertUserProfileApiTests.Jwt(userId.ToString(), "another-signing-key-0123456789-abcdef"), UpsertUserProfileApiTests.Jwt(userId.ToString()) })
            await Status(await Send(token, Body()), 401, "E10002", "Invalid or expired access token.");
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", userId));
        await Status(await Send(jwt, Body()), 401, "E10002", null);
        Assert.Empty(await Rows(userId));

        var (lockedId, lockedJwt) = await CallerAsync();
        await ExecuteAsync(_postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", lockedId));
        await Status(await Send(lockedJwt, Body()), 401, "E10002", null);
    }

    [Fact]
    public async Task TC_ADDRESS_012_concurrent_inserts_cannot_both_take_the_tenth_slot()
    {
        var (userId, jwt) = await CallerAsync();
        for (var i = 0; i < 9; i++) await InsertAsync(jwt, Body($"A{i}"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => Send(jwt, Body($"Race{i}"))));

        Assert.Equal([201, 400, 400], responses.Select(r => (int)r.StatusCode).Order());
        var rows = await Rows(userId);
        Assert.Equal(10, rows.Count);
        Assert.Single(rows, a => a.IsDefault);
    }

    [Fact]
    public async Task TC_ADDRESS_013_a_held_user_lock_times_out_without_a_row()
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
            await Status(await Send(jwt, Body(), client), 408, "E90001", "The server timed out waiting for the request.");
            await transaction.RollbackAsync();
        }

        Assert.Empty(await Rows(userId));
    }

    private sealed class FailingAddresses(IUserAddressRepository inner) : IUserAddressRepository
    {
        public Task<OwnedAddress?> FindOwnedAsync(Guid id, Guid u, CancellationToken ct) => inner.FindOwnedAsync(id, u, ct);
        public Task<int> CountAsync(Guid u, CancellationToken ct) => inner.CountAsync(u, ct);
        public Task<bool> OtherActiveExistsAsync(Guid u, Guid? x, CancellationToken ct) => inner.OtherActiveExistsAsync(u, x, ct);
        public Task ClearDefaultAsync(Guid u, Guid? x, DateTime n, CancellationToken ct) => inner.ClearDefaultAsync(u, x, n, ct);
        public Task<Guid> InsertAsync(Guid u, AddressValues v, string s, bool d, DateTime n, CancellationToken ct) => throw new InvalidOperationException("injected after the default was cleared");
        public Task<bool> UpdateAsync(Guid id, Guid u, AddressValues v, string s, bool d, DateTime n, CancellationToken ct) => inner.UpdateAsync(id, u, v, s, d, n, ct);
        public Task PromoteDefaultAsync(Guid u, DateTime n, CancellationToken ct) => inner.PromoteDefaultAsync(u, n, ct);
    }

    [Fact]
    public async Task TC_ADDRESS_013_an_unexpected_failure_is_500_and_keeps_the_old_default()
    {
        var (userId, jwt) = await CallerAsync();
        var a1 = await InsertAsync(jwt, Body("A1"));
        var client = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<UserAddressRepository>();
            s.AddScoped<IUserAddressRepository>(p => new FailingAddresses(p.GetRequiredService<UserAddressRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Body("New default", isDefault: true), client), 500, null, "An unexpected error occurred.");

        Assert.Equal(a1, await DefaultOf(userId));
        Assert.Single(await Rows(userId));
    }
}
