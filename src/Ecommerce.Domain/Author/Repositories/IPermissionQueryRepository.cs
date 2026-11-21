namespace Ecommerce.Domain.Author.Repositories;

/// <summary>One non-DELETED permission as spec 06 returns it.</summary>
public sealed record PermissionRow(Guid Id, string Name, string Description, string RowStatus);

/// <summary>One non-DELETED scope link of a listed permission; its status is the link's, not the master's.</summary>
public sealed record PermissionLinkRow(Guid Id, Guid PermissionId, Guid ScopeId, string RowStatus);

/// <summary>A scope master referenced by a listed link, in any lifecycle status.</summary>
public sealed record ScopeMasterRow(Guid Id, string Scope, string Name);

/// <summary>The permission reads of spec 06-inquiry-permission, in the caller's read-only snapshot; no lock, no write.</summary>
public interface IPermissionQueryRepository
{
    /// <summary>QUERY - Get total items.</summary>
    Task<long> CountAsync(MasterFilter filter, CancellationToken cancellationToken);

    /// <summary>QUERY - Get permissions: name, then id.</summary>
    Task<IReadOnlyList<PermissionRow>> ListAsync(MasterFilter filter, long offset, long limit, CancellationToken cancellationToken);

    /// <summary>QUERY - Get the scopes of the permissions: their non-DELETED links, by permission then link id.</summary>
    Task<IReadOnlyList<PermissionLinkRow>> ListLinksAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken);

    /// <summary>QUERY - Get scope: the referenced masters, by scope code then id.</summary>
    Task<IReadOnlyList<ScopeMasterRow>> ListScopesAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken);
}
