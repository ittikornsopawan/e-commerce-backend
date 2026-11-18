using System.Data;
using Ecommerce.Infrastructure.Persistence;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Ecommerce.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Ecommerce.Infrastructure.Tests.RegistrationFixtures;

namespace Ecommerce.Infrastructure.Tests;

// The database behavior of 03-reset-password test-cases.md, proven on the real schema.
[Collection(PostgresCollection.Name)]
public sealed class PasswordResetRequestRepositoryTests(PostgresFixture postgres)
{
    private readonly FieldProtector _protector = new(FieldKey);

    private async Task<(Guid Id, string Email)> UserAsync()
    {
        var registration = Registration();
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return (await new RegistrationRepository(db, _protector).AddAsync(registration, CancellationToken.None), registration.EmailAddress);
    }

    private async Task<T> InTransaction<T>(Func<PasswordResetRequestRepository, Task<T>> work)
    {
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        return (await new EfTransactionRunner(db).RunAsync(IsolationLevel.ReadCommitted,
            async _ => Result.Success(await work(new PasswordResetRequestRepository(db, _protector))), CancellationToken.None)).Value;
    }

    [Fact]
    public async Task TC_RESET_004_TC_RESET_005_lookup_finds_only_an_ACTIVE_user_by_the_exact_email()
    {
        var (id, email) = await UserAsync();
        Assert.Equal(id, await InTransaction(r => r.FindActiveUserIdAsync(email, CancellationToken.None)));
        Assert.Null(await InTransaction(r => r.FindActiveUserIdAsync(email.ToUpperInvariant(), CancellationToken.None)));
        await ExecuteAsync(postgres.ConnectionString, "UPDATE authen.m_user SET row_status = 'LOCKED' WHERE id = @id", ("id", id));
        Assert.Null(await InTransaction(r => r.FindActiveUserIdAsync(email, CancellationToken.None)));
        Assert.False(await InTransaction(r => r.LockActiveUserAsync(id, email, CancellationToken.None)));
    }

    [Fact]
    public async Task TC_RESET_009_the_user_row_is_locked_FOR_UPDATE_and_rechecked()
    {
        var (id, email) = await UserAsync();
        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var repository = new PasswordResetRequestRepository(db, _protector);
        Assert.False(await repository.LockActiveUserAsync(id, "other@example.test", CancellationToken.None));
        Assert.True(await repository.LockActiveUserAsync(id, email, CancellationToken.None));

        await using var other = new NpgsqlConnection(postgres.ConnectionString);
        await other.OpenAsync();
        await using var command = new NpgsqlCommand("SET lock_timeout = '300ms'; SELECT id FROM authen.m_user WHERE id = @id FOR UPDATE", other);
        command.Parameters.AddWithValue("id", id);
        Assert.Equal("55P03", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync())).SqlState);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TC_RESET_003_TC_RESET_007_replace_closes_the_active_row_and_count_uses_created_at_of_every_status()
    {
        var (id, _) = await UserAsync();
        var (otherId, _) = await UserAsync();
        var first = DateTime.UtcNow.AddMinutes(-61);
        await InTransaction(r => r.ReplaceActiveResetAsync(otherId, [9], first, first.AddMinutes(30), CancellationToken.None));
        await InTransaction(r => r.ReplaceActiveResetAsync(id, [1], first, first.AddMinutes(30), CancellationToken.None));
        var now = DateTime.UtcNow;
        var newId = await InTransaction(r => r.ReplaceActiveResetAsync(id, [2], now, now.AddMinutes(30), CancellationToken.None));

        await using var db = EcommerceDbContext.Create(postgres.ConnectionString);
        var rows = await db.PasswordResets.Where(r => r.UserId == id).OrderBy(r => r.CreatedAt).ToListAsync();
        Assert.Equal(["INACTIVE", "ACTIVE"], rows.Select(r => r.RowStatus));
        Assert.Equal(id.ToString(), rows[0].UpdatedBy);
        Assert.Equal(now, rows[0].UpdatedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal((newId, id.ToString()), (rows[1].Id, rows[1].CreatedBy));
        Assert.Equal(now.AddMinutes(30), rows[1].ExpiredAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal("ACTIVE", (await db.PasswordResets.SingleAsync(r => r.UserId == otherId)).RowStatus);

        Assert.Equal(1, await InTransaction(r => r.CountRequestsAsync(id, now.AddHours(-1), CancellationToken.None)));
        Assert.Equal(2, await InTransaction(r => r.CountRequestsAsync(id, now.AddHours(-2), CancellationToken.None)));
        // created_at equal to windowStart is outside the window.
        Assert.Equal(0, await InTransaction(r => r.CountRequestsAsync(id, rows[1].CreatedAt, CancellationToken.None)));
    }
}
