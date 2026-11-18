using System.Data;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Infrastructure.Tests;

// The database behavior of 05-refresh-token test-cases.md, proven on the real schema.
[Collection(PostgresCollection.Name)]
public sealed class RefreshTokenRepositoryTests(PostgresFixture postgres)
{
    private readonly FieldProtector _protector = new(FieldKey);

    private async Task<T> InTransaction<T>(Func<RefreshTokenRepository, Task<T>> work)
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return (await new EfTransactionRunner(db).RunAsync(IsolationLevel.ReadCommitted,
            async _ => Result.Success(await work(new RefreshTokenRepository(db, _protector))), CancellationToken.None)).Value;
    }

    private async Task<Guid> UserAsync()
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return await new RegistrationRepository(db, _protector).AddAsync(Registration(), CancellationToken.None);
    }

    private async Task<byte[]> TokenAsync(string table, Guid userId, Guid sessionId, string status = "ACTIVE", int expiresInMinutes = 60)
    {
        var hash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        await ExecuteAsync(postgres.ConnectionString,
            $"INSERT INTO {table} (created_by, row_status, user_id, session_id, token, expired_at) VALUES ('test', @s, @u, @sid, @t, @x)",
            ("s", status), ("u", userId), ("sid", sessionId), ("t", hash), ("x", DateTime.UtcNow.AddMinutes(expiresInMinutes)));
        return hash;
    }

    [Fact]
    public async Task TC_REFRESH_004_owner_lookup_finds_any_status_but_not_an_expired_row()
    {
        var userId = await UserAsync();
        var inactive = await TokenAsync("authen.t_user_refresh_token", userId, Guid.NewGuid(), "INACTIVE");
        var expired = await TokenAsync("authen.t_user_refresh_token", userId, Guid.NewGuid(), expiresInMinutes: -1);

        Assert.Equal(userId, (await InTransaction(r => r.FindOwnerAsync(inactive, DateTime.UtcNow, CancellationToken.None)))!.UserId);
        Assert.Null(await InTransaction(r => r.FindOwnerAsync(expired, DateTime.UtcNow, CancellationToken.None)));
    }

    [Fact]
    public async Task TC_REFRESH_001_reread_and_consume_once()
    {
        var userId = await UserAsync();
        var session = Guid.NewGuid();
        var hash = await TokenAsync("authen.t_user_refresh_token", userId, session);
        var now = DateTime.UtcNow;
        var owner = (await InTransaction(r => r.FindOwnerAsync(hash, now, CancellationToken.None)))!;
        var row = (await InTransaction(r => r.RereadAsync(owner, hash, CancellationToken.None)))!;

        Assert.Equal((session, "ACTIVE", (DateTime?)null), (row.SessionId, row.RowStatus, row.UpdatedAt));
        Assert.True(await InTransaction(r => r.ConsumeAsync(row, hash, now, CancellationToken.None)));
        Assert.False(await InTransaction(r => r.ConsumeAsync(row, hash, now, CancellationToken.None)));
        var after = (await InTransaction(r => r.RereadAsync(owner, hash, CancellationToken.None)))!;
        Assert.Equal("INACTIVE", after.RowStatus);
        Assert.Equal(now, after.UpdatedAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task TC_REFRESH_007_close_session_closes_only_that_session_of_that_user()
    {
        var userId = await UserAsync();
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        foreach (var table in new[] { "authen.t_user_refresh_token", "authen.t_user_access_token" })
        {
            await TokenAsync(table, userId, s1);
            await TokenAsync(table, userId, s1, expiresInMinutes: -5);
            await TokenAsync(table, userId, s2);
        }

        await InTransaction(async r => { await r.CloseSessionAsync(s1, userId, DateTime.UtcNow, CancellationToken.None); return 0; });

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.SessionId == s1 && t.RowStatus == "ACTIVE"));
        Assert.Equal(0, await db.AccessTokens.CountAsync(t => t.SessionId == s1 && t.RowStatus == "ACTIVE"));
        Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.SessionId == s2 && t.RowStatus == "ACTIVE"));
        Assert.Equal(1, await db.AccessTokens.CountAsync(t => t.SessionId == s2 && t.RowStatus == "ACTIVE"));
    }

    [Fact]
    public async Task TC_REFRESH_009_the_user_row_is_locked_and_only_an_active_user_is_returned()
    {
        var userId = await UserAsync();
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();
        Assert.NotNull(await new RefreshTokenRepository(db, _protector).LockActiveUserAsync(userId, CancellationToken.None));

        await using var other = new NpgsqlConnection(postgres.ConnectionString);
        await other.OpenAsync();
        await using var command = new NpgsqlCommand("SET lock_timeout = '300ms'; SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", other);
        command.Parameters.AddWithValue("id", userId);
        Assert.Equal("55P03", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync())).SqlState);
        await transaction.RollbackAsync();

        await ExecuteAsync(postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", userId));
        Assert.Null(await InTransaction(r => r.LockActiveUserAsync(userId, CancellationToken.None)));
    }
}
