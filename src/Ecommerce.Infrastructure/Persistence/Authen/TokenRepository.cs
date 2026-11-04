using Ecommerce.Domain.Authen.Entities;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Infrastructure.Persistence.Authen;

internal sealed class TokenRepository(DbSession session) : ITokenRepository
{
    public Task SaveAccessTokenAsync(UserId userId, byte[] tokenHash, DateTime expiresAtUtc, CancellationToken cancellationToken) =>
        InsertTokenAsync("authen.t_user_access_token", "token", Guid.CreateVersion7(), userId, tokenHash, expiresAtUtc, cancellationToken);

    public Task SaveRefreshTokenAsync(UserId userId, byte[] tokenHash, DateTime expiresAtUtc, CancellationToken cancellationToken) =>
        InsertTokenAsync("authen.t_user_refresh_token", "token", Guid.CreateVersion7(), userId, tokenHash, expiresAtUtc, cancellationToken);

    public async Task AddPasswordResetAsync(PasswordResetToken token, CancellationToken cancellationToken)
    {
        await session.ExecuteAsync(
            """
            UPDATE authen.t_user_reset_password
            SET row_status = 'INACTIVE', updated_by = @actor, updated_at = now() AT TIME ZONE 'UTC'
            WHERE user_id = @userId AND row_status = 'ACTIVE'
            """,
            new { userId = token.UserId.Value, actor = token.UserId.ToString() }, cancellationToken);

        await InsertTokenAsync(
            "authen.t_user_reset_password", "reset_token", token.Id, token.UserId, token.TokenHash, token.ExpiresAtUtc, cancellationToken);
    }

    public async Task<PasswordResetToken?> FindActivePasswordResetAsync(
        byte[] tokenHash, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = await session.QuerySingleOrDefaultAsync<ResetRow>(
            """
            SELECT id AS Id, user_id AS UserId, expired_at AS ExpiresAt
            FROM authen.t_user_reset_password
            WHERE reset_token = @tokenHash AND row_status = 'ACTIVE' AND expired_at > @now
            """,
            new { tokenHash, now = Unspecified(nowUtc) }, cancellationToken);

        return row is null
            ? null
            : PasswordResetToken.Restore(
                row.Id, new UserId(row.UserId), tokenHash, DateTime.SpecifyKind(row.ExpiresAt, DateTimeKind.Utc), consumedAtUtc: null);
    }

    public Task ConsumePasswordResetAsync(PasswordResetToken token, CancellationToken cancellationToken) =>
        session.ExecuteAsync(
            """
            UPDATE authen.t_user_reset_password
            SET row_status = 'INACTIVE', updated_by = @actor, updated_at = now() AT TIME ZONE 'UTC'
            WHERE id = @id
            """,
            new { id = token.Id, actor = token.UserId.ToString() }, cancellationToken);

    public async Task RevokeSessionsAsync(UserId userId, CancellationToken cancellationToken)
    {
        foreach (var table in new[] { "authen.t_user_access_token", "authen.t_user_refresh_token" })
        {
            await session.ExecuteAsync(
                $"""
                UPDATE {table}
                SET row_status = 'INACTIVE', updated_by = @actor, updated_at = now() AT TIME ZONE 'UTC'
                WHERE user_id = @userId AND row_status = 'ACTIVE'
                """,
                new { userId = userId.Value, actor = userId.ToString() }, cancellationToken);
        }
    }

    // table and column come from the constants above, never from user input
    private Task InsertTokenAsync(
        string table, string column, Guid id, UserId userId, byte[] tokenHash, DateTime expiresAtUtc, CancellationToken cancellationToken) =>
        session.ExecuteAsync(
            $"""
            INSERT INTO {table} (id, created_by, created_at, user_id, {column}, expired_at)
            VALUES (@id, @actor, now() AT TIME ZONE 'UTC', @userId, @tokenHash, @expiresAt)
            """,
            new { id, actor = userId.ToString(), userId = userId.Value, tokenHash, expiresAt = Unspecified(expiresAtUtc) },
            cancellationToken);

    // The columns are "timestamp without time zone", which Npgsql only accepts for DateTimeKind.Unspecified.
    // Every value stored there is UTC.
    private static DateTime Unspecified(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

    private sealed class ResetRow
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public DateTime ExpiresAt { get; init; }
    }
}
