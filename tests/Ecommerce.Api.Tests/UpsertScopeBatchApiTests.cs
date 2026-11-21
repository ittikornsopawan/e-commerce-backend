using System.Text.Json;
using System.Text.Json.Nodes;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Author;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Ecommerce.Api.Tests.UpsertScopeApiTests;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/author/08-upsert-scope-batch/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class UpsertScopeBatchApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/scope/upsert/batch";
    private const string Success = "Batch Upsert Scope Success";
    private const string ManageForbidden = "You do not have permission to manage scopes.";

    private static string Batch(params JsonNode[] items) => new JsonObject { ["scopes"] = new JsonArray(items) }.ToJsonString();

    private Task<HttpResponseMessage> Send(string? jwt, string body, HttpClient? client = null) => Post(Path, jwt, body, client);

    private static List<(int Index, Guid ScopeId, string Action)> Results(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["scopes"], data.EnumerateObject().Select(p => p.Name));
        return data.GetProperty("scopes").EnumerateArray().Select(r =>
        {
            Assert.Equal(["index", "scopeId", "action"], r.EnumerateObject().Select(p => p.Name));
            return (r.GetProperty("index").GetInt32(), r.GetProperty("scopeId").GetGuid(), r.GetProperty("action").GetString()!);
        }).ToList();
    }

    private Task<string> ColumnAsync(Guid id, string column) => ScalarAsync<string>($"SELECT {column} FROM author.m_scope WHERE id = @id", id);

    private async Task<long> CodeCountAsync(string code)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM author.m_scope WHERE scope = @s", connection);
        command.Parameters.AddWithValue("s", code);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task TC_SBATCH_001_insert_update_delete_and_archive_in_one_batch()
    {
        var (adminId, jwt) = await AdminAsync();
        var (updated, deleted, archived) = (await ScopeAsync(NewCode(), "ACTIVE"), await ScopeAsync(NewCode(), "ACTIVE"), await ScopeAsync(NewCode(), "ACTIVE"));
        var (inserted, renamed) = (NewCode(), NewCode());

        var results = Results(await Status(await Send(jwt, Batch(
            Body(inserted, "New"),
            Body(renamed, "Renamed", id: updated, rowStatus: "INACTIVE"),
            Body(await ColumnAsync(deleted, "scope"), id: deleted, rowStatus: "DELETED"),
            Body(await ColumnAsync(archived, "scope"), id: archived, rowStatus: "ARCHIVED"))), 200, "S10000", Success));

        Assert.Equal([0, 1, 2, 3], results.Select(r => r.Index));
        Assert.Equal(["INSERT", "UPDATE", "DELETE", "ARCHIVE"], results.Select(r => r.Action));
        Assert.Equal([updated, deleted, archived], results.Skip(1).Select(r => r.ScopeId));
        Assert.Equal((inserted, "ACTIVE", adminId.ToString()), (await ColumnAsync(results[0].ScopeId, "scope"), await ColumnAsync(results[0].ScopeId, "row_status"), await ColumnAsync(results[0].ScopeId, "created_by")));
        Assert.Equal((renamed, "INACTIVE"), (await ColumnAsync(updated, "scope"), await ColumnAsync(updated, "row_status")));
        Assert.Equal(("DELETED", "ARCHIVED"), (await ColumnAsync(deleted, "row_status"), await ColumnAsync(archived, "row_status")));
    }

    public static TheoryData<string, string> Shapes => new()
    {
        { "[]", "Request Body must be an object." },
        { """{"scopes":[],"extra":1}""", "Extra is not a known field." },
        { "{}", "Scopes is a required field." },
        { """{"scopes":"a"}""", "Scopes must be an array." },
        { """{"scopes":[]}""", "Scopes must contain 1 to 100 items." },
        { """{"scopes":[null]}""", "scopes[0]: Scope must be an object." },
        { """{"scopes":[{"scope":"A","name":"n","description":"d","x":1}]}""", "scopes[0]: X is not a known field." },
        { """{"scopes":[{"scope":"A","name":"n","description":"d"},{"scope":"a","name":"","description":"d"}]}""",
          "scopes[1]: Name is a required field, Scope code must be capital letters, digits and _, starting with a letter, at most 64 characters." },
        { """{"scopes":[{"scope":"A","name":"n","description":"d","rowStatus":"DELETED"}]}""", "scopes[0]: Row status must not be DELETED when inserting a scope." }
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task TC_SBATCH_002_request_and_item_validation(string body, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Send(jwt, body), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_SBATCH_002_item_count_duplicates_and_query_keys()
    {
        var (_, jwt) = await AdminAsync();
        var codes = Enumerable.Range(0, 101).Select(_ => NewCode()).ToList();
        await Status(await Send(jwt, Batch(codes.Select(c => (JsonNode)Body(c)).ToArray())), 400, "E10001", "Scopes must contain 1 to 100 items.");
        Assert.Equal(Enumerable.Range(0, 100), Results(await Status(await Send(jwt, Batch(codes.Take(100).Select(c => (JsonNode)Body(c)).ToArray())), 200, "S10000", Success)).Select(r => r.Index));
        await Status(await Post(Path + "?x=1", jwt, Batch(Body(NewCode()))), 400, "E10001", "X is not a known field.");

        var a = await ScopeAsync(NewCode(), "ACTIVE");
        var codeA = await ColumnAsync(a, "scope");
        var upper = Body(NewCode(), id: a);
        upper["id"] = a.ToString().ToUpperInvariant();
        await Status(await Send(jwt, Batch(Body(NewCode()), Body(codeA, id: a), upper)), 400, "E10001", "scopes[2]: Id is duplicated with scopes[1].");
        var same = NewCode();
        await Status(await Send(jwt, Batch(Body(same), Body(NewCode()), Body(same))), 400, "E10001", "scopes[2]: Scope is duplicated with scopes[0].");

        // A DELETED item does not hold its code: the scope is deleted and its code used again by the insert after it.
        var results = Results(await Status(await Send(jwt, Batch(Body(codeA, id: a, rowStatus: "DELETED"), Body(codeA))), 200, "S10000", Success));
        Assert.Equal(["DELETE", "INSERT"], results.Select(r => r.Action));
    }

    [Fact]
    public async Task TC_SBATCH_003_validation_before_the_credential_and_credential_failures()
    {
        await Status(await Send(null, Batch(Body("bad"))), 400, "E10001",
            "scopes[0]: Scope code must be capital letters, digits and _, starting with a letter, at most 64 characters.");
        var code = NewCode();
        await Status(await Send(null, Batch(Body(code))), 401, "E10002", Invalid);
        await Status(await Send((await CallerAsync(null)).Jwt, Batch(Body(code))), 401, "E10002", Invalid);
        var (userId, jwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.t_user_access_token SET row_status = 'INACTIVE' WHERE user_id = @u", ("u", userId));
        await Status(await Send(jwt, Batch(Body(code))), 401, "E10002", Invalid);
        Assert.Equal(0L, await CodeCountAsync(code));
    }

    [Fact]
    public async Task TC_SBATCH_004_first_missing_target_then_one_403()
    {
        var (_, jwt) = await AdminAsync();
        var deleted = await ScopeAsync(NewCode(), "DELETED");
        var kept = await ScopeAsync(NewCode(), "ACTIVE");
        var insert = NewCode();
        var low = Guid.Parse("00000000-0000-4000-8000-000000000001");

        await Status(await Send(jwt, Batch(Body(insert), Body(NewCode(), id: Guid.NewGuid()), Body(await ColumnAsync(kept, "scope"), "Changed", id: kept), Body(NewCode(), id: low))),
            404, "E10004", "scopes[1]: Scope not found.");
        await Status(await Send(jwt, Batch(Body(insert), Body(NewCode(), id: deleted))), 404, "E10004", "scopes[1]: Scope not found.");

        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, other) = await CallerAsync(roles);
            await Status(await Send(other, Batch(Body(NewCode(), id: deleted))), 404, "E10004", "scopes[0]: Scope not found.");
            await Status(await Send(other, Batch(Body(NewCode()), Body(await ColumnAsync(kept, "scope"), id: kept))), 403, "E10009", ManageForbidden);
        }

        Assert.Equal((0L, "seeded"), (await CodeCountAsync(insert), await ColumnAsync(kept, "description")));
    }

    [Fact]
    public async Task TC_SBATCH_005_item_errors_name_their_position_and_roll_back()
    {
        var (_, jwt) = await AdminAsync();
        var first = NewCode();
        var taken = await ScopeAsync(NewCode(), "ARCHIVED");
        var used = await ScopeAsync(NewCode(), "ACTIVE");
        await PermissionScopeAsync(await PermissionAsync(NewPermissionName()), used);
        var (takenCode, usedCode) = (await ColumnAsync(taken, "scope"), await ColumnAsync(used, "scope"));

        await Status(await Send(jwt, Batch(Body(first), Body(takenCode))), 409, "E10003", "scopes[1]: Scope code already exists.");
        await Status(await Send(jwt, Batch(Body(first), Body(usedCode, id: used, rowStatus: "ARCHIVED"))), 409, "E10006", "scopes[1]: Scope is used by permissions.");
        // The code check answers before the in-use check.
        await Status(await Send(jwt, Batch(Body(first), Body(takenCode, id: used, rowStatus: "DELETED"))), 409, "E10003", "scopes[1]: Scope code already exists.");
        Assert.Equal((0L, "ACTIVE"), (await CodeCountAsync(first), await ColumnAsync(used, "row_status")));
    }

    [Fact]
    public async Task TC_SBATCH_006_items_see_the_writes_before_them()
    {
        var (_, jwt) = await AdminAsync();
        var (a, b) = (await ScopeAsync(NewCode(), "ACTIVE"), await ScopeAsync(NewCode(), "LOCKED"));
        var (codeA, codeB) = (await ColumnAsync(a, "scope"), await ColumnAsync(b, "scope"));

        await Status(await Send(jwt, Batch(Body(codeB), Body(codeB, id: b, rowStatus: "DELETED"))), 409, "E10003", "scopes[0]: Scope code already exists.");
        await Status(await Send(jwt, Batch(Body(codeB, id: a), Body(NewCode(), id: b))), 409, "E10003", "scopes[0]: Scope code already exists.");
        Assert.Equal((codeA, "LOCKED"), (await ColumnAsync(a, "scope"), await ColumnAsync(b, "row_status")));
        Assert.Equal(["DELETE", "INSERT"], Results(await Status(await Send(jwt, Batch(Body(codeB, id: b, rowStatus: "DELETED"), Body(codeB))), 200, "S10000", Success)).Select(r => r.Action));
    }

    [Fact]
    public async Task TC_SBATCH_007_concurrent_batches_with_one_code()
    {
        var jwts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AdminAsync()));
        var code = NewCode();
        var responses = await Task.WhenAll(jwts.Select(a => Send(a.Jwt, Batch(Body(NewCode()), Body(code)))));

        Assert.Equal(1, responses.Count(r => (int)r.StatusCode == 200));
        foreach (var conflict in responses.Where(r => (int)r.StatusCode != 200))
            await Status(conflict, 409, "E10003", "scopes[1]: Scope code already exists.");
        Assert.Equal(1L, await CodeCountAsync(code));
    }

    [Fact]
    public async Task TC_SBATCH_008_locks_and_their_order()
    {
        var (adminId, jwt) = await AdminAsync();
        var (a, b) = (await ScopeAsync(NewCode(), "ACTIVE"), await ScopeAsync(NewCode(), "ACTIVE"));
        var (codeA, codeB) = (await ColumnAsync(a, "scope"), await ColumnAsync(b, "scope"));
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        var (connection, transaction) = await HoldAsync("SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE", adminId);
        await Status(await Send(jwt, Batch(Body(codeA, id: a)), client), 408, "E90001", "The server timed out waiting for the request.");
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        // FOR SHARE conflicts only with this API's FOR UPDATE; a non-administrator stops before any write when not blocked.
        var (_, customer) = await CallerAsync(new[] { "CUSTOMER" });
        (connection, transaction) = await HoldAsync("SELECT 1 FROM author.m_scope WHERE id = @id FOR SHARE", b);
        await Status(await Send(customer, Batch(Body(codeA, id: a), Body(codeB, id: b)), client), 408, "E90001", null);
        await transaction.RollbackAsync();
        await connection.DisposeAsync();
        await Status(await Send(customer, Batch(Body(codeA, id: a), Body(codeB, id: b)), client), 403, "E10009", ManageForbidden);

        var (_, other) = await AdminAsync();
        for (var round = 0; round < 5; round++)
        {
            var responses = await Task.WhenAll(Send(jwt, Batch(Body(codeA, "ab", id: a), Body(codeB, "ab", id: b))), Send(other, Batch(Body(codeB, "ba", id: b), Body(codeA, "ba", id: a))));
            foreach (var response in responses) await Status(response, 200, "S10000", Success);
        }
    }

    private sealed class FailingSecondInsert(IScopeRepository inner) : IScopeRepository
    {
        private int _inserts;
        public Task<bool> LockAsync(Guid id, CancellationToken ct) => inner.LockAsync(id, ct);
        public Task<IReadOnlySet<Guid>> LockManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => inner.LockManyAsync(ids, ct);
        public Task<bool> CodeTakenAsync(string scope, Guid? excludeId, CancellationToken ct) => inner.CodeTakenAsync(scope, excludeId, ct);
        public Task<bool> InUseAsync(Guid id, CancellationToken ct) => inner.InUseAsync(id, ct);
        public Task<bool> UpdateAsync(Guid id, ScopeValues values, string by, DateTime now, CancellationToken ct) => inner.UpdateAsync(id, values, by, now, ct);

        public async Task<Guid> InsertAsync(ScopeValues values, string by, DateTime now, CancellationToken ct)
        {
            var id = await inner.InsertAsync(values, by, now, ct);
            return ++_inserts == 2 ? throw new InvalidOperationException("injected after the second insert") : id;
        }
    }

    [Fact]
    public async Task TC_SBATCH_009_an_unexpected_failure_rolls_back_every_item()
    {
        var (_, jwt) = await AdminAsync();
        var (first, second) = (NewCode(), NewCode());
        var client = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<ScopeRepository>();
            s.AddScoped<IScopeRepository>(p => new FailingSecondInsert(p.GetRequiredService<ScopeRepository>()));
        })).CreateClient();

        await Status(await Send(jwt, Batch(Body(first), Body(second)), client), 500, null, "An unexpected error occurred.");
        Assert.Equal((0L, 0L), (await CodeCountAsync(first), await CodeCountAsync(second)));
    }
}
