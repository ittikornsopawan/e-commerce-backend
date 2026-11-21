namespace Ecommerce.Domain.Author.Repositories;

/// <summary>A locked role: its name decides whether the request touches the PLATFORM_ADMIN assignment.</summary>
public sealed record LockedRole(string Name, string RowStatus);

/// <summary>A locked assignment: the single source for its user, role and status.</summary>
public sealed record UserRoleRow(Guid Id, Guid UserId, Guid RoleId, string RowStatus);

/// <summary>The user-role SQL of spec 10-upsert-user-role, inside the caller's transaction.</summary>
public interface IUserRoleRepository
{
    /// <summary>QUERY - Lock the users: the caller and the target, FOR UPDATE in ascending id order; their status by id (any status).</summary>
    Task<IReadOnlyDictionary<Guid, string>> LockUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>QUERY - Lock existing user role: the non-DELETED assignment FOR UPDATE, or null.</summary>
    Task<UserRoleRow?> LockAssignmentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>QUERY - Check duplicate user role: another non-DELETED assignment of the user to the role.</summary>
    Task<bool> TakenAsync(Guid userId, Guid roleId, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>INSERT - Create user role; returns the generated id.</summary>
    Task<Guid> InsertAsync(Guid userId, Guid roleId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update, archive or delete user role, guarded by id and non-DELETED; false when no row.</summary>
    Task<bool> UpdateAsync(Guid id, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Count the platform administrators after the write: ACTIVE assignments of the ACTIVE PLATFORM_ADMIN role whose user is ACTIVE.</summary>
    Task<long> AdministratorsAsync(CancellationToken cancellationToken);
}
