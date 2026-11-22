namespace Ecommerce.Domain.Author.Repositories;

/// <summary>The four nullable filters of spec 14; each disables only itself.</summary>
public sealed record RolePermissionFilter(Guid? Id, Guid? RoleId, Guid? PermissionId, string? RowStatus);

/// <summary>One non-DELETED grant with its role and permission names.</summary>
public sealed record RolePermissionListRow(Guid Id, Guid RoleId, string RoleName, Guid PermissionId, string PermissionName, string RowStatus);

/// <summary>One non-DELETED scope link of a listed grant, with its definition's scope.</summary>
public sealed record RolePermissionScopeListRow(Guid Id, Guid RolePermissionId, Guid PermissionScopeId, Guid ScopeId, string Scope, string RowStatus);

/// <summary>The grant reads of spec 14-inquiry-role-permission, in the caller's read-only snapshot; no lock, no write.</summary>
public interface IRolePermissionQueryRepository
{
    /// <summary>QUERY - Get total items.</summary>
    Task<long> CountAsync(RolePermissionFilter filter, CancellationToken cancellationToken);

    /// <summary>QUERY - Get role permissions: role name, then permission name, then id.</summary>
    Task<IReadOnlyList<RolePermissionListRow>> ListAsync(RolePermissionFilter filter, long offset, long limit, CancellationToken cancellationToken);

    /// <summary>QUERY - Get the scopes of the role permissions: by grant, then scope code, then link id.</summary>
    Task<IReadOnlyList<RolePermissionScopeListRow>> ListScopesAsync(IReadOnlyCollection<Guid> grantIds, CancellationToken cancellationToken);
}
