using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The owner of a presented refresh token: a routing hint until the row is reread under the user lock.</summary>
public sealed record RefreshOwner(Guid RefreshTokenId, Guid UserId);

/// <summary>The refresh-specific reads and writes of spec 05-refresh-token (claims and inserts reuse <see cref="ILoginRepository"/>).</summary>
public interface IRefreshTokenRepository
{
    /// <summary>QUERY - Find the refresh token: any status, expired_at after <paramref name="lookupNowUtc"/>.</summary>
    Task<RefreshOwner?> FindOwnerAsync(byte[] tokenHash, DateTime lookupNowUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Lock the user: the ACTIVE user row FOR UPDATE, decrypted for the JWT user snapshot.</summary>
    Task<LoginUser?> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>QUERY - Reread the refresh token after user lock.</summary>
    Task<RefreshTokenRow?> RereadAsync(RefreshOwner owner, byte[] tokenHash, CancellationToken cancellationToken);

    /// <summary>UPDATE - Use the refresh token once: true only when this ACTIVE, unexpired row was consumed.</summary>
    Task<bool> ConsumeAsync(RefreshTokenRow row, byte[] tokenHash, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Close the refresh tokens, then the access tokens, of the session (reuse).</summary>
    Task CloseSessionAsync(Guid sessionId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}
