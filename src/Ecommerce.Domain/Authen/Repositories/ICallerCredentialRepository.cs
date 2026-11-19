namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The current-credential check of the protected authen APIs: under the user lock (writes) or joined (reads).</summary>
public interface ICallerCredentialRepository
{
    /// <summary>QUERY - Lock the user: the ACTIVE user row FOR UPDATE.</summary>
    Task<bool> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check the current access token: ACTIVE, owned by the user, unexpired at <paramref name="nowUtc"/>.</summary>
    Task<bool> IsCurrentAccessTokenAsync(byte[] accessTokenHash, Guid userId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Check current credential and user (read-only APIs): the stored token joined with the ACTIVE user, no lock.</summary>
    Task<bool> IsCurrentCredentialAsync(byte[] accessTokenHash, Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}
