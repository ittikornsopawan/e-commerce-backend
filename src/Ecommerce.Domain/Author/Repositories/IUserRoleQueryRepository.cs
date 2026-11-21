namespace Ecommerce.Domain.Author.Repositories;

/// <summary>The four nullable filters of spec 11; each disables only itself.</summary>
public sealed record UserRoleFilter(Guid? Id, Guid? UserId, Guid? RoleId, string? RowStatus);

/// <summary>One non-DELETED assignment with its user's display name (nullable) and its role's name.</summary>
public sealed record UserRoleListRow(Guid Id, Guid UserId, string? UserDisplayName, Guid RoleId, string RoleName, string RowStatus);

/// <summary>The assignment reads of spec 11-inquiry-user-role, in the caller's read-only snapshot; no lock, no write.</summary>
public interface IUserRoleQueryRepository
{
    /// <summary>QUERY - Get total items.</summary>
    Task<long> CountAsync(UserRoleFilter filter, CancellationToken cancellationToken);

    /// <summary>QUERY - Get user roles: role name, then display name, then id.</summary>
    Task<IReadOnlyList<UserRoleListRow>> ListAsync(UserRoleFilter filter, long offset, long limit, CancellationToken cancellationToken);
}
