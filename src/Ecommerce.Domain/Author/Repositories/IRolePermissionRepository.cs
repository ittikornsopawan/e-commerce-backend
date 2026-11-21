namespace Ecommerce.Domain.Author.Repositories;

/// <summary>A locked grant of a permission to a role.</summary>
public sealed record RolePermissionRow(Guid Id, Guid RoleId, Guid PermissionId, string RowStatus);

/// <summary>A non-DELETED scope definition of the permission with its master's status (specs 13, 15).</summary>
public sealed record PermissionScopeDefinition(Guid PermissionScopeId, Guid ScopeId, string RowStatus, string ScopeRowStatus);

/// <summary>A locked non-DELETED scope link of a grant.</summary>
public sealed record GrantScopeRow(Guid Id, Guid PermissionScopeId, string RowStatus);

/// <summary>The grant SQL of spec 13-upsert-role-permission, inside the caller's transaction.</summary>
public interface IRolePermissionRepository
{
    /// <summary>QUERY - Lock existing role permission: the non-DELETED grant FOR UPDATE, or null.</summary>
    Task<RolePermissionRow?> LockGrantAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>QUERY - Get the scopes of the permission: every non-DELETED definition, whatever its master's status.</summary>
    Task<IReadOnlyList<PermissionScopeDefinition>> DefinitionsAsync(Guid permissionId, CancellationToken cancellationToken);

    /// <summary>QUERY - Lock the scopes of the role permission: its non-DELETED links FOR UPDATE in ascending id order.</summary>
    Task<IReadOnlyList<GrantScopeRow>> LockScopesAsync(Guid grantId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check duplicate role permission: another non-DELETED grant of the permission to the role.</summary>
    Task<bool> TakenAsync(Guid roleId, Guid permissionId, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>INSERT - Create role permission; returns the generated id.</summary>
    Task<Guid> InsertAsync(Guid roleId, Guid permissionId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update, archive or delete role permission, guarded by id and non-DELETED; false when no row.</summary>
    Task<bool> UpdateAsync(Guid id, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>INSERT - Create role permission scope; returns the generated id.</summary>
    Task<Guid> InsertScopeAsync(Guid grantId, Guid permissionScopeId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update or delete role permission scope, guarded by id, grant and non-DELETED; false when no row.</summary>
    Task<bool> UpdateScopeAsync(Guid id, Guid grantId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Delete the scopes of a deleted role permission.</summary>
    Task DeleteScopesAsync(Guid grantId, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Count active scopes after the write.</summary>
    Task<long> ActiveScopesAsync(Guid grantId, CancellationToken cancellationToken);
}
