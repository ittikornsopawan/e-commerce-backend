namespace Ecommerce.Domain.Author.Repositories;

/// <summary>The user lock of the assignment writers (specs 10, 15), inside the caller's transaction.</summary>
public interface IUserLockRepository
{
    /// <summary>QUERY - Lock the users: the caller and the target, FOR UPDATE in ascending id order; their status by id (any status).</summary>
    Task<IReadOnlyDictionary<Guid, string>> LockUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}
