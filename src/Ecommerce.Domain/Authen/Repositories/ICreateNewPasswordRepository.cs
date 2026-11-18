using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>A reset row found by its token hash: only a routing hint until it is consumed under the user lock.</summary>
public sealed record ResetRoute(Guid ResetId, Guid UserId);

/// <summary>The ACTIVE PASSWORD row of a user.</summary>
public sealed record CurrentPassword(Guid PasswordId, PasswordCredential Credential);

/// <summary>The reads and writes of spec 04-create-new-password, in its order.</summary>
public interface ICreateNewPasswordRepository
{
    /// <summary>QUERY - Find the reset token: ACTIVE and expired_at after <paramref name="lookupNowUtc"/> (autocommit).</summary>
    Task<ResetRoute?> FindResetAsync(byte[] resetTokenHash, DateTime lookupNowUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Lock the user: the ACTIVE user row FOR UPDATE.</summary>
    Task<bool> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>UPDATE - Use the reset token once: true only when exactly this ACTIVE, unexpired row was consumed.</summary>
    Task<bool> ConsumeResetAsync(ResetRoute route, byte[] resetTokenHash, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Get current password.</summary>
    Task<CurrentPassword?> GetCurrentPasswordAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>UPDATE - Make the current password INACTIVE (guarded), then INSERT - Create password. False when the guard matched no row.</summary>
    Task<bool> ReplacePasswordAsync(Guid userId, Guid currentPasswordId, PasswordCredential newPassword, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Close the access tokens, then the refresh tokens, of every session of the user.</summary>
    Task CloseAllTokensAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}
