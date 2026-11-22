namespace Ecommerce.Domain.Author.Repositories;

/// <summary>A role master row: what the upsert needs to decide on it.</summary>
public sealed record RoleValues(string Name, string Description, string RowStatus);

/// <summary>The role SQL of specs 01-upsert-role and 02-upsert-role-batch, inside the caller's transaction.</summary>
public interface IRoleRepository
{
    /// <summary>QUERY - Check existing role: the non-DELETED role FOR UPDATE; its stored name, or null.</summary>
    Task<string?> LockNameAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>QUERY - Lock existing roles (spec 02): the non-DELETED roles among the ids, locked FOR UPDATE in ascending id order; their stored names by id.</summary>
    Task<IReadOnlyDictionary<Guid, string>> LockNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>QUERY - Lock the role (specs 10, 13): the role FOR UPDATE in any status; its name and status, or null.</summary>
    Task<LockedRole?> LockAnyAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>QUERY - Check duplicate name: another non-DELETED role with the exact name.</summary>
    Task<bool> NameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check user in use / Check role permission in use: a non-DELETED binding of either kind.</summary>
    Task<bool> InUseAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>INSERT - Create role; returns the generated id.</summary>
    Task<Guid> InsertAsync(RoleValues values, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update or delete role, guarded by id, non-DELETED and not the system role; false when no row.</summary>
    Task<bool> UpdateAsync(Guid id, RoleValues values, string by, DateTime nowUtc, CancellationToken cancellationToken);
}
