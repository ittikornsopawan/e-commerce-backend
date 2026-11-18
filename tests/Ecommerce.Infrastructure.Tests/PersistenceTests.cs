using System.Data;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ecommerce.Infrastructure.Tests;

// Owner decisions of 2026-11-18: data access is EF Core with LINQ (no raw SQL in handlers or repositories);
// the row locks, REPEATABLE READ and atomic conditional writes of the specs keep their meaning.
[Collection(PostgresCollection.Name)]
public sealed class PersistenceTests(PostgresFixture postgres)
{
    private EcommerceDbContext Context() => EcommerceDbContext.Create(postgres.ConnectionString);

    private async Task<Guid> InsertUser()
    {
        await using var db = Context();
        var id = Guid.NewGuid();
        db.Users.Add(new UserRecord { Id = id, CreatedBy = "test", Username = id.ToByteArray(), EmailAddress = id.ToByteArray(), RowStatus = "ACTIVE" });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task The_real_schema_is_loaded_and_readable_through_LINQ()
    {
        var id = await InsertUser();

        await using var db = Context();

        Assert.True(await db.Users.AnyAsync(u => u.Id == id && u.RowStatus == "ACTIVE"));
    }

    [Fact]
    public async Task ForUpdate_holds_a_row_lock_until_the_transaction_ends()
    {
        var id = await InsertUser();
        await using var holder = Context();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await holder.Users.Where(u => u.Id == id).ForUpdate().ToListAsync();

        // Another connection cannot lock the same row: it times out instead of waiting for ever.
        await using var other = new NpgsqlConnection(postgres.ConnectionString);
        await other.OpenAsync();
        await using var command = new NpgsqlCommand("SET lock_timeout = '300ms'; SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", other);
        command.Parameters.AddWithValue("id", id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync());
        Assert.Equal("55P03", error.SqlState);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task A_query_without_ForUpdate_takes_no_lock()
    {
        var id = await InsertUser();
        await using var holder = Context();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await holder.Users.Where(u => u.Id == id).ToListAsync();

        await using var other = new NpgsqlConnection(postgres.ConnectionString);
        await other.OpenAsync();
        await using var command = new NpgsqlCommand("SET lock_timeout = '300ms'; SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", other);
        command.Parameters.AddWithValue("id", id);

        Assert.Equal(id, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task A_conditional_ExecuteUpdate_wins_once_like_UPDATE_RETURNING()
    {
        var id = await InsertUser();
        await using var db = Context();

        var first = await db.Users.Where(u => u.Id == id && u.RowStatus == "ACTIVE").ExecuteUpdateAsync(s => s.SetProperty(u => u.RowStatus, "INACTIVE"));
        var second = await db.Users.Where(u => u.Id == id && u.RowStatus == "ACTIVE").ExecuteUpdateAsync(s => s.SetProperty(u => u.RowStatus, "INACTIVE"));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task RepeatableRead_keeps_one_snapshot_for_the_whole_transaction()
    {
        var id = await InsertUser();
        await using var reader = Context();
        await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        Assert.Equal("ACTIVE", (await reader.Users.AsNoTracking().SingleAsync(u => u.Id == id)).RowStatus);

        await using (var writer = Context())
        {
            await writer.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.RowStatus, "LOCKED"));
        }

        Assert.Equal("ACTIVE", (await reader.Users.AsNoTracking().SingleAsync(u => u.Id == id)).RowStatus);
        await transaction.RollbackAsync();
    }
}
