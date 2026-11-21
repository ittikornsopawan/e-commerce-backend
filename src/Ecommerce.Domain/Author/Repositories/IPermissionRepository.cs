namespace Ecommerce.Domain.Author.Repositories;

/// <summary>A permission master row as spec 04 writes it.</summary>
public sealed record PermissionValues(string Name, string Description, string RowStatus);

/// <summary>The permission and scope-link SQL of spec 04-upsert-permission, inside the caller's transaction.</summary>
public interface IPermissionRepository
{
    /// <summary>QUERY - Check existing permission: the non-DELETED permission FOR UPDATE.</summary>
    Task<bool> LockAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>QUERY - Check scope: the ACTIVE scope masters among the ids, FOR UPDATE in ascending id order.</summary>
    Task<IReadOnlySet<Guid>> LockActiveScopesAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken);

    /// <summary>QUERY - Check existing permission scope: this permission's non-DELETED links among the ids, FOR UPDATE in id order; their stored scope ids.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> LockLinksAsync(Guid permissionId, IReadOnlyCollection<Guid> linkIds, CancellationToken cancellationToken);

    /// <summary>QUERY - Count permission scope definitions: every non-DELETED link of the permission.</summary>
    Task<long> CountLinksAsync(Guid permissionId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check duplicate name: another non-DELETED permission with the exact name.</summary>
    Task<bool> NameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check role/user permission in use: a non-DELETED role or direct-user grant of the permission.</summary>
    Task<bool> GrantedAsync(Guid permissionId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check duplicate permission scope: another non-DELETED link of the permission to the scope.</summary>
    Task<bool> LinkTakenAsync(Guid permissionId, Guid scopeId, Guid? excludeLinkId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check role/user permission scope in use: a non-DELETED role-scope or direct-user reference to the link.</summary>
    Task<bool> LinkInUseAsync(Guid linkId, CancellationToken cancellationToken);

    /// <summary>INSERT - Create permission; returns the generated id.</summary>
    Task<Guid> InsertAsync(PermissionValues values, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update or delete permission, guarded by id and non-DELETED; false when no row.</summary>
    Task<bool> UpdateAsync(Guid id, PermissionValues values, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>INSERT - Create permission scope; returns the generated id.</summary>
    Task<Guid> InsertLinkAsync(Guid permissionId, Guid scopeId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update or delete permission scope, guarded by id, permission, the immutable scope and non-DELETED; false when no row.</summary>
    Task<bool> UpdateLinkAsync(Guid linkId, Guid permissionId, Guid scopeId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken);
}
