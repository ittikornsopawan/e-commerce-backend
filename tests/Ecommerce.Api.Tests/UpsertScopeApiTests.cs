using System.Text.Json;
using System.Text.Json.Nodes;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Author;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/author/07-upsert-scope/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertScopeApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/scope/upsert";

    internal static JsonObject Body(string scope, string name = "A scope", string description = "Scope description", Guid? id = null, string? rowStatus = null)
    {
        var body = new JsonObject { ["scope"] = scope, ["name"] = name, ["description"] = description };
        if (id is not null) body["id"] = id.ToString();
        if (rowStatus is not null) body["rowStatus"] = rowStatus;
        return body;
    }

    /// <summary>A scope code no other test uses.</summary>
    internal static string NewCode() => "S_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    private Task<HttpResponseMessage> Send(string? jwt, JsonNode body, HttpClient? client = null) => Post(Path, jwt, body.ToJsonString(), client);

    private static Guid ScopeId(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["scopeId"], data.EnumerateObject().Select(p => p.Name));
        return data.GetProperty("scopeId").GetGuid();
    }

    private Task<string> ColumnAsync(Guid id, string column) =>
        ScalarAsync<string>($"SELECT {column} FROM author.m_scope WHERE id = @id", id);

    [Fact]
    public async Task TC_SCOPE_001_insert()
    {
        var (adminId, jwt) = await AdminAsync();
        var code = NewCode();

        var id = ScopeId(await Status(await Send(jwt, Body(code, "Region", "The shops of one region")), 201, "S10000", "Insert Scope Success"));
        Assert.Equal((code, "Region", "The shops of one region", "ACTIVE", adminId.ToString()),
            (await ColumnAsync(id, "scope"), await ColumnAsync(id, "name"), await ColumnAsync(id, "description"), await ColumnAsync(id, "row_status"), await ColumnAsync(id, "created_by")));
    }

    private const string ManageForbidden = "You do not have permission to manage scopes.";
    private const string CodeFormat = "Scope code must be capital letters, digits and _, starting with a letter, at most 64 characters.";

    private async Task<Guid> ScopeWithCodeAsync(string code, string rowStatus = "ACTIVE") => await ScopeAsync(code, rowStatus);

    private async Task<long> CodeCountAsync(string code)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM author.m_scope WHERE scope = @s", connection);
        command.Parameters.AddWithValue("s", code);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task TC_SCOPE_002_update_archive_reopen_and_delete()
    {
        var (adminId, jwt) = await AdminAsync();
        var id = await ScopeWithCodeAsync(NewCode());
        var code = NewCode();

        Assert.Equal(id, ScopeId(await Status(await Send(jwt, Body(code, "Renamed", "Changed", id, "LOCKED")), 200, "S10000", "Update Scope Success")));
        Assert.Equal((code, "Renamed", "Changed", "LOCKED", adminId.ToString(), "seed"),
            (await ColumnAsync(id, "scope"), await ColumnAsync(id, "name"), await ColumnAsync(id, "description"), await ColumnAsync(id, "row_status"),
             await ColumnAsync(id, "updated_by"), await ColumnAsync(id, "created_by")));

        await Status(await Send(jwt, Body(code, id: id, rowStatus: "ARCHIVED")), 200, "S10000", "Archive Scope Success");
        await Status(await Send(jwt, Body(code, id: id)), 200, "S10000", "Update Scope Success");
        Assert.Equal("ACTIVE", await ColumnAsync(id, "row_status"));

        await Status(await Send(jwt, Body(code, id: id, rowStatus: "DELETED")), 204, null, null);
        await Status(await Send(jwt, Body(code, id: id)), 404, "E10004", "Scope not found.");
        await Status(await Send(jwt, Body(code)), 201, "S10000", "Insert Scope Success");

        var archived = ScopeId(await Status(await Send(jwt, Body(NewCode(), rowStatus: "ARCHIVED")), 201, "S10000", "Insert Scope Success"));
        Assert.Equal("ARCHIVED", await ColumnAsync(archived, "row_status"));
    }

    public static TheoryData<string, string> InvalidBodies => new()
    {
        { "[]", "Request Body must be an object." },
        { """{"scope":"A","name":"n","description":"d","code":1}""", "Code is not a known field." },
        { """{"scope":"A","name":"n","description":"d","scope":"B"}""", "Scope must not be duplicated." },
        { """{"scope":"A","name":1,"description":"d"}""", "Name must be a string." },
        { """{"scope":" ","name":"","description":null}""", "Scope, Name and Description are required fields." },
        { """{"scope":"A","description":"d"}""", "Name is a required field." },
        { """{"scope":"region","name":"n","description":"d"}""", CodeFormat },
        { """{"scope":"1A","name":"n","description":"d"}""", CodeFormat },
        { """{"scope":" A","name":"n","description":"d"}""", CodeFormat },
        { """{"scope":"A","name":"n","description":"d","rowStatus":"DELETED"}""", "Row status must not be DELETED when inserting a scope." },
        { """{"scope":"A","name":"n","description":"d","rowStatus":"GONE"}""", "Row Status must be ACTIVE, INACTIVE, DELETED, LOCKED or ARCHIVED." },
        { """{"id":null,"scope":"A","name":"n","description":"d"}""", "Id must be a valid UUID." },
        { """{"id":"x","scope":"a","name":"","description":"d"}""", "Id must be a valid UUID, Name is a required field, " + "Scope code must be capital letters, digits and _, starting with a letter, at most 64 characters." }
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task TC_SCOPE_003_input_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Post(Path, jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_SCOPE_003_lengths_and_query_keys()
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, Body("A" + new string('B', 64))), 400, "E10001", CodeFormat);
        await Status(await Send(jwt, Body(NewCode(), new string('n', 65))), 400, "E10001", "Name must be at most 64 characters.");
        await Status(await Send(jwt, Body(NewCode(), description: new string('d', 257))), 400, "E10001", "Description must be at most 256 characters.");
        await Status(await Post(Path + "?x=1", jwt, Body(NewCode()).ToJsonString()), 400, "E10001", "X is not a known field.");

        var exact = "A" + Guid.NewGuid().ToString("N").ToUpperInvariant() + new string('B', 31);
        await Status(await Send(jwt, Body(exact, new string('n', 64), new string('d', 256))), 201, "S10000", "Insert Scope Success");
    }

    [Fact]
    public async Task TC_SCOPE_004_validation_before_the_credential_and_credential_failures()
    {
        await Status(await Send(null, Body("bad")), 400, "E10001", CodeFormat);

        var code = NewCode();
        await Status(await Send(null, Body(code)), 401, "E10002", Invalid);
        await Status(await Send("not-a-jwt", Body(code)), 401, "E10002", Invalid);
        await Status(await Send((await CallerAsync(null)).Jwt, Body(code)), 401, "E10002", Invalid);
        var (userId, jwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", userId));
        await Status(await Send(jwt, Body(code)), 401, "E10002", Invalid);
        Assert.Equal(0L, await CodeCountAsync(code));
    }

    [Fact]
    public async Task TC_SCOPE_005_only_a_platform_administrator_after_the_scope_lookup()
    {
        var id = await ScopeWithCodeAsync(NewCode());
        var deleted = await ScopeWithCodeAsync(NewCode(), "DELETED");
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            await Status(await Send(jwt, Body(NewCode())), 403, "E10009", ManageForbidden);
            await Status(await Send(jwt, Body(NewCode(), id: id)), 403, "E10009", ManageForbidden);
            await Status(await Send(jwt, Body(NewCode(), id: deleted)), 404, "E10004", "Scope not found.");
        }

        Assert.Equal("seeded", await ColumnAsync(id, "description"));
    }

    [Fact]
    public async Task TC_SCOPE_006_duplicate_code()
    {
        var (_, jwt) = await AdminAsync();
        foreach (var status in new[] { "ACTIVE", "INACTIVE", "LOCKED", "ARCHIVED" })
        {
            var taken = NewCode();
            await ScopeWithCodeAsync(taken, status);
            await Status(await Send(jwt, Body(taken)), 409, "E10003", "Scope code already exists.");
            await Status(await Send(jwt, Body(taken, id: await ScopeWithCodeAsync(NewCode()))), 409, "E10003", "Scope code already exists.");
        }

        var own = NewCode();
        var id = await ScopeWithCodeAsync(own);
        await Status(await Send(jwt, Body(own, "Own code", id: id)), 200, "S10000", "Update Scope Success");

        var deletedCode = NewCode();
        await ScopeWithCodeAsync(deletedCode, "DELETED");
        await Status(await Send(jwt, Body(deletedCode)), 201, "S10000", "Insert Scope Success");

        // The code check comes before the in-use check: an in-use scope archived under a taken code answers the code 409.
        var used = await ScopeWithCodeAsync(NewCode());
        await PermissionScopeAsync(await PermissionAsync(NewPermissionName()), used);
        var takenCode = NewCode();
        await ScopeWithCodeAsync(takenCode);
        await Status(await Send(jwt, Body(takenCode, id: used, rowStatus: "ARCHIVED")), 409, "E10003", "Scope code already exists.");
    }

    [Fact]
    public async Task TC_SCOPE_007_in_use_blocks_archive_and_delete()
    {
        var (_, jwt) = await AdminAsync();
        var used = await ScopeWithCodeAsync(NewCode());
        await PermissionScopeAsync(await PermissionAsync(NewPermissionName()), used, "LOCKED");
        var code = await ColumnAsync(used, "scope");

        foreach (var status in new[] { "ARCHIVED", "DELETED" })
            await Status(await Send(jwt, Body(code, id: used, rowStatus: status)), 409, "E10006", "Scope is used by permissions.");
        await Status(await Send(jwt, Body(code, id: used, rowStatus: "INACTIVE")), 200, "S10000", "Update Scope Success");

        // A DELETED link no longer uses it.
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.t_permission_scope SET row_status = 'DELETED' WHERE scope_id = @s", ("s", used));
        await Status(await Send(jwt, Body(code, id: used, rowStatus: "DELETED")), 204, null, null);
    }

    [Fact]
    public async Task TC_SCOPE_008_concurrent_inserts_of_one_code()
    {
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var code = NewCode();
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Body(code))));

        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 201));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 201))
            await Status(conflict, 409, "E10003", "Scope code already exists.");
        Assert.Equal(1L, await CodeCountAsync(code));
    }

    [Fact]
    public async Task TC_SCOPE_009_the_caller_and_target_locks()
    {
        var (adminId, jwt) = await AdminAsync();
        var id = await ScopeWithCodeAsync(NewCode());
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        var (connection, transaction) = await HoldAsync("SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE", adminId);
        await Status(await Send(jwt, Body(NewCode(), id: id), client), 408, "E90001", "The server timed out waiting for the request.");
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        // FOR SHARE conflicts only with this API's FOR UPDATE; a non-administrator stops before any write when not blocked.
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        (connection, transaction) = await HoldAsync("SELECT 1 FROM author.m_scope WHERE id = @id FOR SHARE", id);
        await Status(await Send(customer, Body(NewCode(), id: id), client), 408, "E90001", null);
        await transaction.RollbackAsync();
        await connection.DisposeAsync();
        await Status(await Send(customer, Body(NewCode(), id: id), client), 403, "E10009", ManageForbidden);
        Assert.Equal("seeded", await ColumnAsync(id, "description"));
    }

    private sealed class FailingInsert(IScopeRepository inner) : IScopeRepository
    {
        public Task<bool> LockAsync(Guid id, CancellationToken ct) => inner.LockAsync(id, ct);
        public Task<IReadOnlySet<Guid>> LockManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.LockManyAsync(ids, ct);
        public Task<bool> CodeTakenAsync(string scope, Guid? excludeId, CancellationToken ct) => inner.CodeTakenAsync(scope, excludeId, ct);
        public Task<bool> InUseAsync(Guid id, CancellationToken ct) => inner.InUseAsync(id, ct);
        public Task<bool> UpdateAsync(Guid id, ScopeValues values, string by, DateTime now, CancellationToken ct) => inner.UpdateAsync(id, values, by, now, ct);

        public async Task<Guid> InsertAsync(ScopeValues values, string by, DateTime now, CancellationToken ct)
        {
            await inner.InsertAsync(values, by, now, ct);
            throw new InvalidOperationException("injected after the insert");
        }
    }

    [Fact]
    public async Task TC_SCOPE_010_an_unexpected_failure_rolls_back()
    {
        var (_, jwt) = await AdminAsync();
        var code = NewCode();
        var client = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<ScopeRepository>();
            s.AddScoped<IScopeRepository>(p => new FailingInsert(p.GetRequiredService<ScopeRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Body(code), client), 500, null, "An unexpected error occurred.");
        Assert.Equal(0L, await CodeCountAsync(code));
    }
}
