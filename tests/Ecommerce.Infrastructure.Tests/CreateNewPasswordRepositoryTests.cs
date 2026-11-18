using System.Data;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Infrastructure.Tests;

// The database behavior of 04-create-new-password test-cases.md, proven on the real schema.
[Collection(PostgresCollection.Name)]
public sealed class CreateNewPasswordRepositoryTests(PostgresFixture postgres)
{
    private readonly FieldProtector _protector = new(FieldKey);

    private async Task<Guid> UserAsync()
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return await new RegistrationRepository(db, _protector).AddAsync(Registration(), CancellationToken.None);
    }

    private async Task<T> InTransaction<T>(Func<CreateNewPasswordRepository, Task<T>> work)
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return (await new EfTransactionRunner(db).RunAsync(IsolationLevel.ReadCommitted,
            async _ => Result.Success(await work(new CreateNewPasswordRepository(db))), CancellationToken.None)).Value;
    }

    private async Task<byte[]> ResetAsync(Guid userId, DateTime expiredAt, string rowStatus = "ACTIVE")
    {
        var hash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        await ExecuteAsync(postgres.ConnectionString,
            "INSERT INTO authen.t_user_reset_password (created_by, row_status, user_id, reset_token, expired_at) VALUES ('test', @s, @u, @t, @x)",
            ("s", rowStatus), ("u", userId), ("t", hash), ("x", expiredAt));
        return hash;
    }

    [Fact]
    public async Task TC_NEWPW_006_lookup_and_consumption_need_ACTIVE_and_strictly_later_expiry()
    {
        var userId = await UserAsync();
        var now = DateTime.UtcNow;
        var hash = await ResetAsync(userId, now.AddMinutes(30));
        var inactive = await ResetAsync(userId, now.AddMinutes(30), "INACTIVE");

        var route = await InTransaction(r => r.FindResetAsync(hash, now, CancellationToken.None));
        Assert.Equal(userId, route!.UserId);
        Assert.Null(await InTransaction(r => r.FindResetAsync(inactive, now, CancellationToken.None)));
        Assert.Null(await InTransaction(r => r.FindResetAsync(hash, now.AddMinutes(30), CancellationToken.None))); // equality is expired
        Assert.False(await InTransaction(r => r.ConsumeResetAsync(route, hash, now.AddMinutes(30), CancellationToken.None)));
    }

    [Fact]
    public async Task TC_NEWPW_005_a_token_is_consumed_once()
    {
        var userId = await UserAsync();
        var now = DateTime.UtcNow;
        var hash = await ResetAsync(userId, now.AddMinutes(30));
        var route = (await InTransaction(r => r.FindResetAsync(hash, now, CancellationToken.None)))!;

        Assert.True(await InTransaction(r => r.ConsumeResetAsync(route, hash, now, CancellationToken.None)));
        Assert.False(await InTransaction(r => r.ConsumeResetAsync(route, hash, now, CancellationToken.None)));
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var row = await db.PasswordResets.SingleAsync(r => r.Id == route.ResetId);
        Assert.Equal(("INACTIVE", userId.ToString()), (row.RowStatus, row.UpdatedBy));
    }

    [Fact]
    public async Task TC_NEWPW_001_password_is_replaced_and_every_active_token_of_the_user_is_closed()
    {
        var userId = await UserAsync();
        var otherId = await UserAsync();
        foreach (var (user, status, minutes) in new[] { (userId, "ACTIVE", 15), (userId, "ACTIVE", -5), (userId, "INACTIVE", 15), (otherId, "ACTIVE", 15) })
        {
            foreach (var table in new[] { "authen.t_user_access_token", "authen.t_user_refresh_token" })
            {
                await ExecuteAsync(postgres.ConnectionString,
                    $"INSERT INTO {table} (created_by, row_status, user_id, session_id, token, expired_at) VALUES ('test', @s, @u, @sid, @t, @x)",
                    ("s", status), ("u", user), ("sid", Guid.NewGuid()), ("t", Guid.NewGuid().ToByteArray()), ("x", DateTime.UtcNow.AddMinutes(minutes)));
            }
        }

        var now = DateTime.UtcNow;
        var current = (await InTransaction(r => r.GetCurrentPasswordAsync(userId, CancellationToken.None)))!;
        var replaced = await InTransaction(r => r.ReplacePasswordAsync(userId, current.PasswordId, new PasswordCredential([7, 7], new byte[16]), now, CancellationToken.None));
        await InTransaction(async r => { await r.CloseAllTokensAsync(userId, now, CancellationToken.None); return 0; });

        Assert.True(replaced);
        Assert.False(await InTransaction(r => r.ReplacePasswordAsync(userId, current.PasswordId, new PasswordCredential([8], new byte[16]), now, CancellationToken.None)));
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var passwords = await db.UserPasswords.Where(p => p.UserId == userId).ToListAsync();
        var old = passwords.Single(p => p.Id == current.PasswordId);
        Assert.Equal(("INACTIVE", userId.ToString()), (old.RowStatus, old.UpdatedBy));
        var active = passwords.Single(p => p.RowStatus == "ACTIVE");
        Assert.Equal([7, 7], active.Password);
        Assert.Equal(userId.ToString(), active.CreatedBy);
        Assert.Equal(0, await db.AccessTokens.CountAsync(t => t.UserId == userId && t.RowStatus == "ACTIVE"));
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RowStatus == "ACTIVE"));
        Assert.Equal(2, await db.AccessTokens.CountAsync(t => t.UserId == userId && t.UpdatedBy == userId.ToString()));
        Assert.Equal(1, await db.AccessTokens.CountAsync(t => t.UserId == otherId && t.RowStatus == "ACTIVE"));
        Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.UserId == otherId && t.RowStatus == "ACTIVE"));
    }

    [Fact]
    public async Task TC_NEWPW_010_the_user_row_is_locked_FOR_UPDATE()
    {
        var userId = await UserAsync();
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();
        Assert.True(await new CreateNewPasswordRepository(db).LockActiveUserAsync(userId, CancellationToken.None));

        await using var other = new NpgsqlConnection(postgres.ConnectionString);
        await other.OpenAsync();
        await using var command = new NpgsqlCommand("SET lock_timeout = '300ms'; SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", other);
        command.Parameters.AddWithValue("id", userId);
        Assert.Equal("55P03", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync())).SqlState);
        await transaction.RollbackAsync();
    }
}
