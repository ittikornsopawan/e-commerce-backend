namespace Ecommerce.Domain.Author.Repositories;

/// <summary>A locked direct grant of a permission to a user; PermissionScopeId is null for a permission without scopes.</summary>
public sealed record UserPermissionRow(Guid Id, Guid UserId, Guid PermissionId, Guid? PermissionScopeId, string RowStatus);

/// <summary>The direct-grant SQL of spec 15-upsert-user-permission, inside the caller's transaction.</summary>
public interface IUserPermissionRepository
{
    /// <summary>QUERY - Lock existing user permission: the non-DELETED grant FOR UPDATE, or null.</summary>
    Task<UserPermissionRow?> LockGrantAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>QUERY - Check duplicate user permission: another non-DELETED grant of the user, permission and scope (NULL matches NULL).</summary>
    Task<bool> TakenAsync(Guid userId, Guid permissionId, Guid? permissionScopeId, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>INSERT - Create user permission; returns the generated id.</summary>
    Task<Guid> InsertAsync(Guid userId, Guid permissionId, Guid? permissionScopeId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update, archive or delete user permission, guarded by id and non-DELETED; false when no row.</summary>
    Task<bool> UpdateAsync(Guid id, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);
}
