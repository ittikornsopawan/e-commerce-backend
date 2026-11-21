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

// e-commerce/doc/api/author/06-inquiry-permission/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class InquiryPermissionApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/permission";
    private const string Success = "Inquiry Permission Success";

    private sealed record Link(Guid PermissionScopeId, Guid ScopeId, string Scope, string ScopeName, string RowStatus);

    private sealed record Item(Guid PermissionId, string Name, string Description, List<Link> Scopes, string RowStatus);

    /// <summary>The five data keys, each item's five keys and each scope's five keys, in the spec's order.</summary>
    private static (List<Item> Items, long PageIndex, long PageSize, long TotalItems, long TotalPages) Data(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["items", "pageIndex", "pageSize", "totalItems", "totalPages"], data.EnumerateObject().Select(p => p.Name));
        var items = data.GetProperty("items").EnumerateArray().Select(i =>
        {
            Assert.Equal(["permissionId", "name", "description", "scopes", "rowStatus"], i.EnumerateObject().Select(p => p.Name));
            var scopes = i.GetProperty("scopes").EnumerateArray().Select(l =>
            {
                Assert.Equal(["permissionScopeId", "scopeId", "scope", "scopeName", "rowStatus"], l.EnumerateObject().Select(p => p.Name));
                return new Link(l.GetProperty("permissionScopeId").GetGuid(), l.GetProperty("scopeId").GetGuid(), l.GetProperty("scope").GetString()!,
                    l.GetProperty("scopeName").GetString()!, l.GetProperty("rowStatus").GetString()!);
            }).ToList();
            return new Item(i.GetProperty("permissionId").GetGuid(), i.GetProperty("name").GetString()!, i.GetProperty("description").GetString()!,
                scopes, i.GetProperty("rowStatus").GetString()!);
        }).ToList();
        return (items, data.GetProperty("pageIndex").GetInt64(), data.GetProperty("pageSize").GetInt64(),
            data.GetProperty("totalItems").GetInt64(), data.GetProperty("totalPages").GetInt64());
    }

    [Fact]
    public async Task TC_PERMQ_001_items_with_their_scopes_sorted_and_deleted_excluded()
    {
        var (_, jwt) = await AdminAsync();
        // A prefix only this test uses (lower-case, two parts), so the search isolates its rows.
        var prefix = "q" + Guid.NewGuid().ToString("N")[..12];
        var b = await PermissionAsync(prefix + ".b");
        var a = await PermissionAsync(prefix + ".a", "LOCKED");
        await PermissionAsync(prefix + ".c", "DELETED");

        // Scopes of a sort by the master's code, whatever the link order; a DELETED link is left out, a closed master is kept.
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (zulu, alpha) = (await ScopeAsync($"z_{tag}", "ACTIVE"), await ScopeAsync($"a_{tag}", "LOCKED"));
        var zuluLink = await PermissionScopeAsync(a, zulu, "INACTIVE");
        var alphaLink = await PermissionScopeAsync(a, alpha);
        await PermissionScopeAsync(a, await ScopeAsync(), "DELETED");

        var data = Data(await Status(await Get($"{Path}?search={prefix}", jwt), 200, "S10000", Success));
        Assert.Equal([a, b], data.Items.Select(i => i.PermissionId));
        Assert.Equal((prefix + ".a", "seeded", "LOCKED"), (data.Items[0].Name, data.Items[0].Description, data.Items[0].RowStatus));
        Assert.Equal([new Link(alphaLink, alpha, $"a_{tag}", $"Name a_{tag}", "ACTIVE"), new Link(zuluLink, zulu, $"z_{tag}", $"Name z_{tag}", "INACTIVE")], data.Items[0].Scopes);
        Assert.Empty(data.Items[1].Scopes);
        Assert.Equal((1L, 10L, 2L, 1L), (data.PageIndex, data.PageSize, data.TotalItems, data.TotalPages));
    }

    private const string ReadForbidden = "You do not have permission to read permissions.";

    private static string Prefix() => "q" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task TC_PERMQ_002_by_id_combines_with_the_other_filters()
    {
        var (_, jwt) = await AdminAsync();
        var name = Prefix() + ".x";
        var id = await PermissionAsync(name, "INACTIVE");
        var deleted = await PermissionAsync(Prefix() + ".x", "DELETED");

        var data = Data(await Status(await Get($"{Path}?id={id}", jwt), 200, "S10000", Success));
        Assert.Equal([(id, name, "INACTIVE")], data.Items.Select(i => (i.PermissionId, i.Name, i.RowStatus)));
        Assert.Equal((1L, 1L), (data.TotalItems, data.TotalPages));

        foreach (var query in new[] { $"id={id}&rowStatus=ACTIVE", $"id={id}&search=no_such_{name}", $"id={deleted}", $"id={Guid.NewGuid()}" })
        {
            var empty = Data(await Status(await Get($"{Path}?{query}", jwt), 200, "S10000", Success));
            Assert.Equal((0, 1L, 10L, 0L, 0L), (empty.Items.Count, empty.PageIndex, empty.PageSize, empty.TotalItems, empty.TotalPages));
        }
    }

    [Fact]
    public async Task TC_PERMQ_003_search_name_or_description_case_insensitive_with_wildcards()
    {
        var (_, jwt) = await AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..12];
        var byName = await PermissionAsync($"p{tag}.x");
        var byDescription = await PermissionAsync(Prefix() + ".y");
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.m_permission SET description = @d WHERE id = @id", ("d", $"Reads {tag.ToUpperInvariant()} items"), ("id", byDescription));
        var underscore = await PermissionAsync(Prefix() + ".z");
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE author.m_permission SET description = @d WHERE id = @id", ("d", $"{tag}_9"), ("id", underscore));

        Assert.Equal(new HashSet<Guid> { byName, byDescription, underscore },
            Data(await Status(await Get($"{Path}?search={tag.ToUpperInvariant()}&pageSize=100", jwt), 200, "S10000", Success)).Items.Select(i => i.PermissionId).ToHashSet());
        Assert.Equal([underscore], Data(await Status(await Get($"{Path}?search={tag}_9", jwt), 200, "S10000", Success)).Items.Select(i => i.PermissionId));

        var all = Data(await Status(await Get(Path, jwt), 200, "S10000", Success)).TotalItems;
        Assert.Equal(all, Data(await Status(await Get($"{Path}?search=", jwt), 200, "S10000", Success)).TotalItems);
    }

    [Fact]
    public async Task TC_PERMQ_004_row_status_filter()
    {
        var (_, jwt) = await AdminAsync();
        var prefix = Prefix();
        var ids = new Dictionary<string, Guid>();
        foreach (var status in new[] { "ACTIVE", "INACTIVE", "LOCKED", "ARCHIVED", "DELETED" })
            ids[status] = await PermissionAsync($"{prefix}.{status.ToLowerInvariant()}", status);

        foreach (var status in new[] { "ACTIVE", "INACTIVE", "LOCKED", "ARCHIVED" })
            Assert.Equal([ids[status]], Data(await Status(await Get($"{Path}?search={prefix}&rowStatus={status}", jwt), 200, "S10000", Success)).Items.Select(i => i.PermissionId));
        Assert.Equal(4L, Data(await Status(await Get($"{Path}?search={prefix}", jwt), 200, "S10000", Success)).TotalItems);
    }

    [Fact]
    public async Task TC_PERMQ_005_pagination_keeps_each_permissions_scopes()
    {
        var (_, jwt) = await AdminAsync();
        var prefix = Prefix();
        var ids = new List<Guid>();
        var links = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await PermissionAsync($"{prefix}.{i}"));
            links.Add(await PermissionScopeAsync(ids[i], await ScopeAsync()));
        }

        var second = Data(await Status(await Get($"{Path}?search={prefix}&pageIndex=2&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal([ids[2]], second.Items.Select(i => i.PermissionId));
        Assert.Equal([links[2]], second.Items[0].Scopes.Select(l => l.PermissionScopeId));
        Assert.Equal((2L, 2L, 3L, 2L), (second.PageIndex, second.PageSize, second.TotalItems, second.TotalPages));

        var first = Data(await Status(await Get($"{Path}?search={prefix}&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal([[links[0]], [links[1]]], first.Items.Select(i => i.Scopes.Select(l => l.PermissionScopeId).ToList()));

        var beyond = Data(await Status(await Get($"{Path}?search={prefix}&pageIndex=9&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal((0, 9L, 3L, 2L), (beyond.Items.Count, beyond.PageIndex, beyond.TotalItems, beyond.TotalPages));
        Assert.Equal(100L, Data(await Status(await Get($"{Path}?pageSize=100", jwt), 200, "S10000", Success)).PageSize);
    }

    [Theory]
    [MemberData(nameof(InquiryRoleApiTests.InvalidQueries), MemberType = typeof(InquiryRoleApiTests))]
    public async Task TC_PERMQ_006_query_validation(string query, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Get(Path + query, jwt), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_PERMQ_006_no_body_and_validation_before_the_credential()
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Get(Path, jwt, "{}"), 400, "E10001", "Request Body must be empty.");
        await Status(await Get($"{Path}?pageSize=0", null), 400, "E10001", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100.");
    }

    [Fact]
    public async Task TC_PERMQ_007_credential_failures()
    {
        await Status(await Get(Path, null), 401, "E10002", Invalid);
        await Status(await Get(Path, "not-a-jwt"), 401, "E10002", Invalid);
        await Status(await Get(Path, (await CallerAsync(null)).Jwt), 401, "E10002", Invalid);
        var (userId, jwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'INACTIVE' WHERE id = @u", ("u", userId));
        await Status(await Get(Path, jwt), 401, "E10002", Invalid);
    }

    [Fact]
    public async Task TC_PERMQ_008_only_a_platform_administrator()
    {
        var id = await PermissionAsync(Prefix() + ".x");
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            await Status(await Get($"{Path}?id={id}", jwt), 403, "E10009", ReadForbidden);
            await Status(await Get($"{Path}?id={Guid.NewGuid()}", jwt), 403, "E10009", ReadForbidden);
        }
    }

    [Fact]
    public async Task TC_PERMQ_009_no_lock_and_no_write()
    {
        var (adminId, jwt) = await AdminAsync();
        var id = await PermissionAsync(Prefix() + ".x");
        var scope = await ScopeAsync();
        var link = await PermissionScopeAsync(id, scope);
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        foreach (var (sql, target) in new[]
                 {
                     ("SELECT 1 FROM author.m_permission WHERE id = @id FOR UPDATE", id),
                     ("SELECT 1 FROM author.t_permission_scope WHERE id = @id FOR UPDATE", link),
                     ("SELECT 1 FROM author.m_scope WHERE id = @id FOR UPDATE", scope),
                     ("SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE", adminId)
                 })
        {
            var (connection, transaction) = await HoldAsync(sql, target);
            Assert.Single(Data(await Status(await Get($"{Path}?id={id}", jwt, client: client), 200, "S10000", Success)).Items[0].Scopes);
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }

        Assert.Null(await ScalarAsync<object>("SELECT updated_by FROM author.m_permission WHERE id = @id", id) as string);
    }

    [Fact]
    public async Task TC_PERMQ_010_a_held_table_lock_times_out()
    {
        var (_, jwt) = await AdminAsync();
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();
        await using var connection = new NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE author.m_permission IN ACCESS EXCLUSIVE MODE", connection, transaction))
            await command.ExecuteNonQueryAsync();

        await Status(await Get(Path, jwt, client: client), 408, "E90001", "The server timed out waiting for the request.");
        await transaction.RollbackAsync();
    }

    /// <summary>A repository that drops a page row, or a link's master, or counts every master query.</summary>
    private sealed class Faulty(IPermissionQueryRepository inner, string fault) : IPermissionQueryRepository
    {
        public static int MasterQueries;
        public Task<long> CountAsync(MasterFilter filter, CancellationToken ct) => inner.CountAsync(filter, ct);
        public async Task<IReadOnlyList<PermissionRow>> ListAsync(MasterFilter filter, long offset, long limit, CancellationToken ct) =>
            fault == "page" ? (await inner.ListAsync(filter, offset, limit, ct)).Skip(1).ToList() : await inner.ListAsync(filter, offset, limit, ct);
        public Task<IReadOnlyList<PermissionLinkRow>> ListLinksAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken ct) => inner.ListLinksAsync(permissionIds, ct);

        public async Task<IReadOnlyList<ScopeMasterRow>> ListScopesAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken ct)
        {
            Interlocked.Increment(ref MasterQueries);
            var masters = await inner.ListScopesAsync(scopeIds, ct);
            return fault == "master" ? masters.Skip(1).ToList() : masters;
        }
    }

    private HttpClient FaultyClient(string fault) => Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
    {
        s.AddScoped<PermissionQueryRepository>();
        s.AddScoped<IPermissionQueryRepository>(p => new Faulty(p.GetRequiredService<PermissionQueryRepository>(), fault));
    })).CreateClient();

    [Fact]
    public async Task TC_PERMQ_011_a_page_or_master_that_disagrees_is_an_error()
    {
        var (_, jwt) = await AdminAsync();
        var id = await PermissionAsync(Prefix() + ".x");
        await PermissionScopeAsync(id, await ScopeAsync());

        await Status(await Get(Path, jwt, client: FaultyClient("page")), 500, null, "An unexpected error occurred.");
        await Status(await Get($"{Path}?id={id}", jwt, client: FaultyClient("master")), 500, null, "An unexpected error occurred.");
    }

    [Fact]
    public async Task TC_PERMQ_012_no_master_query_without_links()
    {
        var (_, jwt) = await AdminAsync();
        var id = await PermissionAsync(Prefix() + ".x");
        var client = FaultyClient("none");
        var before = Faulty.MasterQueries;

        var data = Data(await Status(await Get($"{Path}?id={id}", jwt, client: client), 200, "S10000", Success));
        Assert.Equal([0], data.Items.Select(i => i.Scopes.Count));
        Assert.Equal(before, Faulty.MasterQueries);
    }
}
