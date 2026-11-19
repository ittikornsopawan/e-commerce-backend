using System.Data;
using Ecommerce.Infrastructure.Persistence;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Shared.Results;
using Microsoft.EntityFrameworkCore;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Infrastructure.Tests;

// The database behavior of 06-logout test-cases.md, proven on the real schema.
[Collection(PostgresCollection.Name)]
public sealed class LogoutRepositoryTests(PostgresFixture postgres)
{
    private readonly FieldProtector _protector = new(FieldKey);

    private async Task<Guid> UserAsync()
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return await new RegistrationRepository(db, _protector).AddAsync(Registration(), CancellationToken.None);
    }

    private async Task<byte[]> TokenAsync(string table, Guid userId, Guid sessionId, string status = "ACTIVE", int minutes = 15)
    {
        var hash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray();
        await ExecuteAsync(postgres.ConnectionString,
            $"INSERT INTO {table} (created_by, row_status, user_id, session_id, token, expired_at) VALUES ('test', @s, @u, @sid, @t, @x)",
            ("s", status), ("u", userId), ("sid", sessionId), ("t", hash), ("x", DateTime.UtcNow.AddMinutes(minutes)));
        return hash;
    }

    private async Task<T> InTransaction<T>(Func<LogoutRepository, Task<T>> work)
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return (await new EfTransactionRunner(db).RunAsync(IsolationLevel.ReadCommitted,
            async _ => Result.Success(await work(new LogoutRepository(db))), CancellationToken.None)).Value;
    }

    [Fact]
    public async Task TC_LOGOUT_004_the_session_is_found_only_for_an_active_unexpired_row_of_that_user()
    {
        var userId = await UserAsync();
        var otherId = await UserAsync();
        var session = Guid.NewGuid();
        var active = await TokenAsync("authen.t_user_access_token", userId, session);
        var inactive = await TokenAsync("authen.t_user_access_token", userId, session, "INACTIVE");
        var expired = await TokenAsync("authen.t_user_access_token", userId, session, minutes: -1);
        var now = DateTime.UtcNow;

        Assert.Equal(session, await InTransaction(r => r.GetSessionAsync(active, userId, now, CancellationToken.None)));
        Assert.Null(await InTransaction(r => r.GetSessionAsync(active, otherId, now, CancellationToken.None)));
        Assert.Null(await InTransaction(r => r.GetSessionAsync(inactive, userId, now, CancellationToken.None)));
        Assert.Null(await InTransaction(r => r.GetSessionAsync(expired, userId, now, CancellationToken.None)));
    }

    [Fact]
    public async Task TC_LOGOUT_001_close_session_closes_only_that_session_of_that_user_and_counts_access_rows()
    {
        var userId = await UserAsync();
        var otherId = await UserAsync();
        var (s1, s2) = (Guid.NewGuid(), Guid.NewGuid());
        foreach (var table in new[] { "authen.t_user_access_token", "authen.t_user_refresh_token" })
        {
            await TokenAsync(table, userId, s1);
            await TokenAsync(table, userId, s1, minutes: -5);
            await TokenAsync(table, userId, s2);
            await TokenAsync(table, otherId, s1);
        }

        Assert.Equal(2, await InTransaction(r => r.CloseSessionAsync(s1, userId, DateTime.UtcNow, CancellationToken.None)));

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        Assert.Equal(0, await db.AccessTokens.CountAsync(t => t.UserId == userId && t.SessionId == s1 && t.RowStatus == "ACTIVE"));
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.SessionId == s1 && t.RowStatus == "ACTIVE"));
        Assert.Equal(1, await db.AccessTokens.CountAsync(t => t.UserId == userId && t.SessionId == s2 && t.RowStatus == "ACTIVE"));
        Assert.Equal(1, await db.AccessTokens.CountAsync(t => t.UserId == otherId && t.SessionId == s1 && t.RowStatus == "ACTIVE"));
        Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.UserId == otherId && t.SessionId == s1 && t.RowStatus == "ACTIVE"));
    }
}
