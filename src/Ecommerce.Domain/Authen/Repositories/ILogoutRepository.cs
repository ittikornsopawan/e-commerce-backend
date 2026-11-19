namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The reads and writes of spec 06-logout, all under the user lock.</summary>
public interface ILogoutRepository
{
    /// <summary>QUERY - Lock the user: the ACTIVE user row FOR UPDATE.</summary>
    Task<bool> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>QUERY - Get the session of the access token: ACTIVE, owned by the user, unexpired at <paramref name="nowUtc"/>.</summary>
    Task<Guid?> GetSessionAsync(byte[] accessTokenHash, Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Close the access tokens, then the refresh tokens, of the session. Returns the closed access rows.</summary>
    Task<int> CloseSessionAsync(Guid sessionId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}
