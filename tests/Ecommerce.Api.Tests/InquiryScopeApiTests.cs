using System.Text.Json;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Author;
using Ecommerce.Infrastructure.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Api.Tests;

// e-commerce/doc/api/author/09-inquiry-scope/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class InquiryScopeApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/scope";
    private const string Success = "Inquiry Scope Success";

    private sealed record Item(Guid ScopeId, string Scope, string Name, string Description, string RowStatus);

    private static (List<Item> Items, long PageIndex, long PageSize, long TotalItems, long TotalPages) Data(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["items", "pageIndex", "pageSize", "totalItems", "totalPages"], data.EnumerateObject().Select(p => p.Name));
        var items = data.GetProperty("items").EnumerateArray().Select(i =>
        {
            Assert.Equal(["scopeId", "scope", "name", "description", "rowStatus"], i.EnumerateObject().Select(p => p.Name));
            return new Item(i.GetProperty("scopeId").GetGuid(), i.GetProperty("scope").GetString()!, i.GetProperty("name").GetString()!,
                i.GetProperty("description").GetString()!, i.GetProperty("rowStatus").GetString()!);
        }).ToList();
        return (items, data.GetProperty("pageIndex").GetInt64(), data.GetProperty("pageSize").GetInt64(),
            data.GetProperty("totalItems").GetInt64(), data.GetProperty("totalPages").GetInt64());
    }

    private static string Prefix() => "Q" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    [Fact]
    public async Task TC_SCOPEQ_001_filters_sort_and_exclude_deleted()
    {
        var (_, jwt) = await AdminAsync();
        var prefix = Prefix();
        var b = await ScopeAsync(prefix + "_B", "ACTIVE");
        var a = await ScopeAsync(prefix + "_A", "LOCKED");
        await ScopeAsync(prefix + "_C", "DELETED");

        var data = Data(await Status(await Get($"{Path}?search={prefix}", jwt), 200, "S10000", Success));
        Assert.Equal([new Item(a, prefix + "_A", $"Name {prefix}_A", "seeded", "LOCKED"), new Item(b, prefix + "_B", $"Name {prefix}_B", "seeded", "ACTIVE")], data.Items);
        Assert.Equal((1L, 10L, 2L, 1L), (data.PageIndex, data.PageSize, data.TotalItems, data.TotalPages));
    }

    private const string ReadForbidden = "You do not have permission to read scopes.";

    [Fact]
    public async Task TC_SCOPEQ_002_by_id_search_and_row_status()
    {
        var (_, jwt) = await AdminAsync();
        var prefix = Prefix();
        var ids = new Dictionary<string, Guid>();
        foreach (var status in new[] { "ACTIVE", "INACTIVE", "LOCKED", "ARCHIVED", "DELETED" })
            ids[status] = await ScopeAsync($"{prefix}_{status}", status);

        Assert.Equal([ids["LOCKED"]], Data(await Status(await Get($"{Path}?id={ids["LOCKED"]}", jwt), 200, "S10000", Success)).Items.Select(i => i.ScopeId));
        foreach (var query in new[] { $"id={ids["LOCKED"]}&rowStatus=ACTIVE", $"id={ids["DELETED"]}", $"id={Guid.NewGuid()}" })
            Assert.Equal((0, 0L, 0L), (Data(await Status(await Get($"{Path}?{query}", jwt), 200, "S10000", Success)) is var d ? (d.Items.Count, d.TotalItems, d.TotalPages) : default));

        foreach (var status in new[] { "ACTIVE", "INACTIVE", "LOCKED", "ARCHIVED" })
            Assert.Equal([ids[status]], Data(await Status(await Get($"{Path}?search={prefix}&rowStatus={status}", jwt), 200, "S10000", Success)).Items.Select(i => i.ScopeId));
        Assert.Equal(4L, Data(await Status(await Get($"{Path}?search={prefix.ToLowerInvariant()}", jwt), 200, "S10000", Success)).TotalItems);

        // The description is searched too, and _ keeps its ILIKE meaning.
        var tag = Guid.NewGuid().ToString("N")[..12];
        var described = await ScopeAsync(Prefix(), "ACTIVE");
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.m_scope SET description = @d WHERE id = @id", ("d", $"{tag}_9"), ("id", described));
        Assert.Equal([described], Data(await Status(await Get($"{Path}?search={tag}_9", jwt), 200, "S10000", Success)).Items.Select(i => i.ScopeId));
    }

    [Fact]
    public async Task TC_SCOPEQ_003_pagination()
    {
        var (_, jwt) = await AdminAsync();
        var prefix = Prefix();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await ScopeAsync($"{prefix}_{i}", "ACTIVE"));

        var second = Data(await Status(await Get($"{Path}?search={prefix}&pageIndex=2&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal([ids[2]], second.Items.Select(i => i.ScopeId));
        Assert.Equal((2L, 2L, 3L, 2L), (second.PageIndex, second.PageSize, second.TotalItems, second.TotalPages));
        var beyond = Data(await Status(await Get($"{Path}?search={prefix}&pageIndex=9&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal((0, 3L, 2L), (beyond.Items.Count, beyond.TotalItems, beyond.TotalPages));
    }

    [Theory]
    [MemberData(nameof(InquiryRoleApiTests.InvalidQueries), MemberType = typeof(InquiryRoleApiTests))]
    public async Task TC_SCOPEQ_004_query_validation(string query, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Get(Path + query, jwt), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_SCOPEQ_005_body_validation_order_credentials_and_403()
    {
        var (_, admin) = await AdminAsync();
        await Status(await Get(Path, admin, "{}"), 400, "E10001", "Request Body must be empty.");
        await Status(await Get($"{Path}?pageSize=0", null), 400, "E10001", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100.");
        await Status(await Get(Path, null), 401, "E10002", Invalid);
        await Status(await Get(Path, (await CallerAsync(null)).Jwt), 401, "E10002", Invalid);

        var id = await ScopeAsync(Prefix(), "ACTIVE");
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            await Status(await Get($"{Path}?id={id}", jwt), 403, "E10009", ReadForbidden);
            await Status(await Get($"{Path}?id={Guid.NewGuid()}", jwt), 403, "E10009", ReadForbidden);
        }
    }

    [Fact]
    public async Task TC_SCOPEQ_006_no_lock_timeout_and_a_disagreeing_page()
    {
        var (adminId, jwt) = await AdminAsync();
        var id = await ScopeAsync(Prefix(), "ACTIVE");
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        var (connection, transaction) = await HoldAsync("SELECT 1 FROM author.m_scope WHERE id = @id FOR UPDATE", id);
        Assert.Single(Data(await Status(await Get($"{Path}?id={id}", jwt, client: client), 200, "S10000", Success)).Items);
        await transaction.RollbackAsync();
        await connection.DisposeAsync();

        await using (var table = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await table.OpenAsync();
            await using var tx = await table.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand("LOCK TABLE author.m_scope IN ACCESS EXCLUSIVE MODE", table, tx))
                await command.ExecuteNonQueryAsync();
            await Status(await Get(Path, jwt, client: client), 408, "E90001", "The server timed out waiting for the request.");
            await tx.RollbackAsync();
        }

        var faulty = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<ScopeQueryRepository>();
            s.AddScoped<IScopeQueryRepository>(p => new ShortPage(p.GetRequiredService<ScopeQueryRepository>()));
        })).CreateClient();
        await Status(await Get(Path, jwt, client: faulty), 500, null, "An unexpected error occurred.");
    }

    private sealed class ShortPage(IScopeQueryRepository inner) : IScopeQueryRepository
    {
        public Task<long> CountAsync(MasterFilter filter, CancellationToken ct) => inner.CountAsync(filter, ct);
        public async Task<IReadOnlyList<ScopeRow>> ListAsync(MasterFilter filter, long offset, long limit, CancellationToken ct) =>
            (await inner.ListAsync(filter, offset, limit, ct)).Skip(1).ToList();
    }
}
