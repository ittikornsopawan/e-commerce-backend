namespace Ecommerce.Domain.Author.Repositories;

/// <summary>The four nullable filters of spec 16; each disables only itself.</summary>
public sealed record UserPermissionFilter(Guid? Id, Guid? UserId, Guid? PermissionId, string? RowStatus);

/// <summary>One non-DELETED direct grant with its user's display name (nullable), its permission's name and its scope (all null without one).</summary>
public sealed record UserPermissionListRow(
    Guid Id, Guid UserId, string? UserDisplayName, Guid PermissionId, string PermissionName,
    Guid? PermissionScopeId, Guid? ScopeId, string? Scope, string RowStatus);

/// <summary>The direct-grant reads of spec 16-inquiry-user-permission, in the caller's read-only snapshot; no lock, no write.</summary>
public interface IUserPermissionQueryRepository
{
    /// <summary>QUERY - Get total items.</summary>
    Task<long> CountAsync(UserPermissionFilter filter, CancellationToken cancellationToken);

    /// <summary>QUERY - Get user permissions: permission name, display name, scope code (no scope last), id.</summary>
    Task<IReadOnlyList<UserPermissionListRow>> ListAsync(UserPermissionFilter filter, long offset, long limit, CancellationToken cancellationToken);
}
