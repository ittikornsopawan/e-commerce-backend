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

// e-commerce/doc/api/author/03-inquiry-role/test-cases.md, through the real HTTP pipeline and database.
[Collection(PostgresCollection.Name)]
public sealed class InquiryRoleApiTests(PostgresFixture postgres, WebApplicationFactory<Program> factory)
    : AuthorApiTests(postgres, factory), IClassFixture<WebApplicationFactory<Program>>
{
    private const string Path = "/v1/author/role";
    private const string Success = "Inquiry Role Success";

    /// <summary>The five data keys, and each item's four keys, in the spec's order.</summary>
    private static (List<(Guid Id, string Name, string Description, string RowStatus)> Items, long PageIndex, long PageSize, long TotalItems, long TotalPages) Data(JsonElement root)
    {
        var data = root.GetProperty("data");
        Assert.Equal(["items", "pageIndex", "pageSize", "totalItems", "totalPages"], data.EnumerateObject().Select(p => p.Name));
        var items = data.GetProperty("items").EnumerateArray().Select(i =>
        {
            Assert.Equal(["roleId", "name", "description", "rowStatus"], i.EnumerateObject().Select(p => p.Name));
            return (i.GetProperty("roleId").GetGuid(), i.GetProperty("name").GetString()!, i.GetProperty("description").GetString()!, i.GetProperty("rowStatus").GetString()!);
        }).ToList();
        return (items, data.GetProperty("pageIndex").GetInt64(), data.GetProperty("pageSize").GetInt64(),
            data.GetProperty("totalItems").GetInt64(), data.GetProperty("totalPages").GetInt64());
    }

    [Fact]
    public async Task TC_ROLEQ_001_filters_sort_and_exclude_deleted()
    {
        var (_, jwt) = await AdminAsync();
        // A prefix only this test uses, so the search isolates its rows in the shared database.
        var prefix = NewName();
        var b = await RoleAsync(prefix + "_B");
        var a = await RoleAsync(prefix + "_A", "LOCKED");
        await RoleAsync(prefix + "_C", "DELETED");

        var data = Data(await Status(await Get($"{Path}?search={prefix}", jwt), 200, "S10000", Success));
        Assert.Equal([(a, prefix + "_A", "seeded", "LOCKED"), (b, prefix + "_B", "seeded", "ACTIVE")], data.Items);
        Assert.Equal((1L, 10L, 2L, 1L), (data.PageIndex, data.PageSize, data.TotalItems, data.TotalPages));
    }

    [Fact]
    public async Task TC_ROLEQ_002_by_id_combines_with_the_other_filters()
    {
        var (_, jwt) = await AdminAsync();
        var name = NewName();
        var id = await RoleAsync(name, "INACTIVE");
        var deleted = await RoleAsync(NewName(), "DELETED");

        var data = Data(await Status(await Get($"{Path}?id={id}", jwt), 200, "S10000", Success));
        Assert.Equal([(id, name, "seeded", "INACTIVE")], data.Items);
        Assert.Equal((1L, 1L), (data.TotalItems, data.TotalPages));

        // The id is one more filter, not a separate path: a second filter that misses empties the list.
        foreach (var query in new[] { $"id={id}&rowStatus=ACTIVE", $"id={id}&search=NO_SUCH_{name}", $"id={deleted}", $"id={Guid.NewGuid()}" })
        {
            var empty = Data(await Status(await Get($"{Path}?{query}", jwt), 200, "S10000", Success));
            Assert.Equal((0, 1L, 10L, 0L, 0L), (empty.Items.Count, empty.PageIndex, empty.PageSize, empty.TotalItems, empty.TotalPages));
        }
    }

    private async Task<Guid> RoleWithDescriptionAsync(string name, string description)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(Postgres.ConnectionString,
            "INSERT INTO author.m_role (id, created_by, name, description) VALUES (@id, 'seed', @n, @d)", ("id", id), ("n", name), ("d", description));
        return id;
    }

    [Fact]
    public async Task TC_ROLEQ_003_search_name_or_description_case_insensitive_with_wildcards()
    {
        var (_, jwt) = await AdminAsync();
        var tag = Guid.NewGuid().ToString("N")[..12];
        var byName = await RoleWithDescriptionAsync($"R_{tag.ToUpperInvariant()}_X", "plain");
        var byDescription = await RoleWithDescriptionAsync(NewName(), $"Manages {tag} items");
        var underscore = await RoleWithDescriptionAsync(NewName(), $"{tag}_9");

        Assert.Equal(new HashSet<Guid> { byName, byDescription, underscore },
            Data(await Status(await Get($"{Path}?search={tag.ToUpperInvariant()}&pageSize=100", jwt), 200, "S10000", Success)).Items.Select(i => i.Id).ToHashSet());

        // % and _ keep their ILIKE meaning: "_9" matches any one character then 9.
        Assert.Equal([underscore], Data(await Status(await Get($"{Path}?search={tag}_9", jwt), 200, "S10000", Success)).Items.Select(i => i.Id));
        Assert.Equal(3L, Data(await Status(await Get($"{Path}?search={tag}%25", jwt), 200, "S10000", Success)).TotalItems);

        // An empty search is a pattern of "%%": every role, the same total as no search.
        var all = Data(await Status(await Get(Path, jwt), 200, "S10000", Success)).TotalItems;
        Assert.Equal(all, Data(await Status(await Get($"{Path}?search=", jwt), 200, "S10000", Success)).TotalItems);
    }

    [Fact]
    public async Task TC_ROLEQ_004_row_status_filter()
    {
        var (_, jwt) = await AdminAsync();
        var prefix = NewName();
        var ids = new Dictionary<string, Guid>();
        foreach (var status in new[] { "ACTIVE", "INACTIVE", "LOCKED", "ARCHIVED", "DELETED" })
            ids[status] = await RoleAsync($"{prefix}_{status}", status);

        foreach (var status in new[] { "ACTIVE", "INACTIVE", "LOCKED", "ARCHIVED" })
            Assert.Equal([ids[status]], Data(await Status(await Get($"{Path}?search={prefix}&rowStatus={status}", jwt), 200, "S10000", Success)).Items.Select(i => i.Id));

        Assert.Equal(4L, Data(await Status(await Get($"{Path}?search={prefix}", jwt), 200, "S10000", Success)).TotalItems);
    }

    [Fact]
    public async Task TC_ROLEQ_005_pagination()
    {
        var (_, jwt) = await AdminAsync();
        var prefix = NewName();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await RoleAsync($"{prefix}_{i}"));

        var second = Data(await Status(await Get($"{Path}?search={prefix}&pageIndex=2&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal([ids[2]], second.Items.Select(i => i.Id));
        Assert.Equal((2L, 2L, 3L, 2L), (second.PageIndex, second.PageSize, second.TotalItems, second.TotalPages));

        // Beyond the last page: no 404, empty items, the totals kept.
        var beyond = Data(await Status(await Get($"{Path}?search={prefix}&pageIndex=9&pageSize=2", jwt), 200, "S10000", Success));
        Assert.Equal((0, 9L, 3L, 2L), (beyond.Items.Count, beyond.PageIndex, beyond.TotalItems, beyond.TotalPages));

        // A huge offset that still fits BIGINT is an empty page, not an error.
        var far = Data(await Status(await Get($"{Path}?search={prefix}&pageIndex=92233720368547758&pageSize=100", jwt), 200, "S10000", Success));
        Assert.Equal((0, 3L), (far.Items.Count, far.TotalItems));

        Assert.Equal(100L, Data(await Status(await Get($"{Path}?pageSize=100", jwt), 200, "S10000", Success)).PageSize);
    }

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { "?id=abc", "Id must be a valid UUID." },
        { "?id=", "Id must be a valid UUID." },
        { "?pageIndex=0", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100." },
        { "?pageSize=101", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100." },
        { "?pageIndex=", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100." },
        { "?pageIndex=1.5", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100." },
        { "?pageIndex=9223372036854775807&pageSize=100", "Page Index is too large for Page Size." },
        { "?rowStatus=DELETED", "Row status must be one of ACTIVE, INACTIVE, LOCKED, ARCHIVED." },
        { "?rowStatus=active", "Row status must be one of ACTIVE, INACTIVE, LOCKED, ARCHIVED." },
        { "?name=A", "Name is not a known field." },
        { "?search=a&search=b", "Search must not be duplicated." },
        { "?id=abc&pageSize=0&rowStatus=GONE", "Id must be a valid UUID, Page Index and Page Size must be greater than 0, Page Size must not be greater than 100, Row status must be one of ACTIVE, INACTIVE, LOCKED, ARCHIVED." }
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task TC_ROLEQ_006_query_validation(string query, string message)
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Get(Path + query, jwt), 400, "E10001", message);
    }

    [Fact]
    public async Task TC_ROLEQ_006_no_body()
    {
        var (_, jwt) = await AdminAsync();
        await Status(await Get(Path, jwt, "{}"), 400, "E10001", "Request Body must be empty.");
    }

    [Fact]
    public async Task TC_ROLEQ_007_validation_before_the_credential() =>
        await Status(await Get($"{Path}?pageSize=0", null), 400, "E10001", "Page Index and Page Size must be greater than 0, Page Size must not be greater than 100.");

    [Fact]
    public async Task TC_ROLEQ_008_credential_failures()
    {
        await Status(await Get(Path, null), 401, "E10002", Invalid);
        await Status(await Get(Path, "not-a-jwt"), 401, "E10002", Invalid);
        await Status(await Get(Path, (await CallerAsync(null)).Jwt), 401, "E10002", Invalid);
        await Status(await Get(Path, (await CallerAsync(new[] { "PLATFORM_ADMIN", "PLATFORM_ADMIN" })).Jwt), 401, "E10002", Invalid);

        var (userId, jwt) = await AdminAsync();
        await ExecuteAsync(Postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'INACTIVE' WHERE id = @u", ("u", userId));
        await Status(await Get(Path, jwt), 401, "E10002", Invalid);
    }

    [Fact]
    public async Task TC_ROLEQ_009_only_a_platform_administrator()
    {
        var id = await RoleAsync(NewName());
        foreach (var roles in new object[] { Array.Empty<string>(), new[] { "CUSTOMER" }, new[] { "platform_admin" } })
        {
            var (_, jwt) = await CallerAsync(roles);
            // The same 403 for an existing id and an unknown one: nothing about the roles is revealed.
            await Status(await Get($"{Path}?id={id}", jwt), 403, "E10009", "You do not have permission to read roles.");
            await Status(await Get($"{Path}?id={Guid.NewGuid()}", jwt), 403, "E10009", "You do not have permission to read roles.");
        }
    }

    [Fact]
    public async Task TC_ROLEQ_010_no_lock_and_no_write()
    {
        var (adminId, jwt) = await AdminAsync();
        var name = NewName();
        var id = await RoleAsync(name);
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();

        // Row locks held by another session do not block the read: the inquiry takes no lock.
        foreach (var (sql, target) in new[] { ("SELECT 1 FROM author.m_role WHERE id = @id FOR UPDATE", id), ("SELECT 1 FROM authen.m_user WHERE id = @id FOR UPDATE", adminId) })
        {
            var (connection, transaction) = await HoldAsync(sql, target);
            Assert.Single(Data(await Status(await Get($"{Path}?id={id}", jwt, client: client), 200, "S10000", Success)).Items);
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }

        Assert.Equal((name, "seeded", "ACTIVE", "seed", (string?)null), await RowAsync(id));
    }

    [Fact]
    public async Task TC_ROLEQ_011_a_held_table_lock_times_out()
    {
        var (_, jwt) = await AdminAsync();
        var client = Factory.WithWebHostBuilder(w => w.UseSetting("Database:CommandTimeoutSeconds", "1")).CreateClient();
        await using var connection = new NpgsqlConnection(Postgres.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("LOCK TABLE author.m_role IN ACCESS EXCLUSIVE MODE", connection, transaction))
            await command.ExecuteNonQueryAsync();

        await Status(await Get(Path, jwt, client: client), 408, "E90001", "The server timed out waiting for the request.");
        await transaction.RollbackAsync();
    }

    private sealed class ShortPage(IRoleQueryRepository inner) : IRoleQueryRepository
    {
        public Task<long> CountAsync(MasterFilter filter, CancellationToken ct) => inner.CountAsync(filter, ct);

        public async Task<IReadOnlyList<RoleRow>> ListAsync(MasterFilter filter, long offset, long limit, CancellationToken ct) =>
            (await inner.ListAsync(filter, offset, limit, ct)).Skip(1).ToList();
    }

    [Fact]
    public async Task TC_ROLEQ_012_a_page_that_disagrees_with_the_count_is_an_error()
    {
        var (_, jwt) = await AdminAsync();
        await RoleAsync(NewName());
        var client = Factory.WithWebHostBuilder(w => w.ConfigureTestServices(s =>
        {
            s.AddScoped<RoleQueryRepository>();
            s.AddScoped<IRoleQueryRepository>(p => new ShortPage(p.GetRequiredService<RoleQueryRepository>()));
        })).CreateClient();

        await Status(await Get(Path, jwt, client: client), 500, null, "An unexpected error occurred.");
    }
}
