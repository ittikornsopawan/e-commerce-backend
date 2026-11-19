namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The current-credential check every protected authen API runs under the user lock (specs 07 to 10).</summary>
public interface ICallerCredentialRepository
{
    /// <summary>QUERY - Lock the user: the ACTIVE user row FOR UPDATE.</summary>
    Task<bool> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check the current access token: ACTIVE, owned by the user, unexpired at <paramref name="nowUtc"/>.</summary>
    Task<bool> IsCurrentAccessTokenAsync(byte[] accessTokenHash, Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}
