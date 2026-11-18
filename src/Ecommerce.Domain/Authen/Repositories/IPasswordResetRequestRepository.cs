namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The reads and writes of spec 03-reset-password, in its order.</summary>
public interface IPasswordResetRequestRepository
{
    /// <summary>QUERY - Find the active user by email address (autocommit, before the transaction).</summary>
    Task<Guid?> FindActiveUserIdAsync(string emailAddress, CancellationToken cancellationToken);

    /// <summary>QUERY - Lock the user: FOR UPDATE, rechecking ACTIVE and the same email. False when no longer eligible.</summary>
    Task<bool> LockActiveUserAsync(Guid userId, string emailAddress, CancellationToken cancellationToken);

    /// <summary>QUERY - Count the requests of the last hour: reset rows of any status created after <paramref name="windowStartUtc"/>.</summary>
    Task<int> CountRequestsAsync(Guid userId, DateTime windowStartUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Make the old reset token INACTIVE, then INSERT - Create reset token. Returns the new reset id.</summary>
    Task<Guid> ReplaceActiveResetAsync(Guid userId, byte[] resetTokenHash, DateTime nowUtc, DateTime expiredAtUtc, CancellationToken cancellationToken);
}
