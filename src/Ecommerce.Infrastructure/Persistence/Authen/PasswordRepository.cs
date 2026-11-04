using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Infrastructure.Persistence.Authen;

internal sealed class PasswordRepository(DbSession session) : IPasswordRepository
{
    public async Task<PasswordCredential?> GetActiveAsync(UserId userId, CancellationToken cancellationToken)
    {
        var row = await session.QuerySingleOrDefaultAsync<CredentialRow>(
            """
            SELECT password AS Hash, password_salt AS Salt
            FROM authen.m_user_password
            WHERE user_id = @userId AND row_status = 'ACTIVE'
            ORDER BY created_at DESC
            LIMIT 1
            """,
            new { userId = userId.Value }, cancellationToken);

        return row is null ? null : new PasswordCredential(row.Hash, row.Salt);
    }

    public async Task SetAsync(UserId userId, PasswordCredential credential, CancellationToken cancellationToken)
    {
        await session.ExecuteAsync(
            """
            UPDATE authen.m_user_password
            SET row_status = 'INACTIVE', updated_by = @actor, updated_at = now() AT TIME ZONE 'UTC'
            WHERE user_id = @userId AND row_status = 'ACTIVE'
            """,
            new { userId = userId.Value, actor = userId.ToString() }, cancellationToken);

        await session.ExecuteAsync(
            """
            INSERT INTO authen.m_user_password (created_by, created_at, row_type, user_id, password, password_salt)
            VALUES (@actor, now() AT TIME ZONE 'UTC', 'PASSWORD', @userId, @hash, @salt)
            """,
            new { userId = userId.Value, actor = userId.ToString(), hash = credential.Hash, salt = credential.Salt },
            cancellationToken);
    }

    private sealed class CredentialRow
    {
        public byte[] Hash { get; init; } = [];
        public byte[] Salt { get; init; } = [];
    }
}
