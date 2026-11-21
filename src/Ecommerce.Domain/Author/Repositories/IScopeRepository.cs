namespace Ecommerce.Domain.Author.Repositories;

/// <summary>A scope master row as spec 07 writes it.</summary>
public sealed record ScopeValues(string Scope, string Name, string Description, string RowStatus);

/// <summary>The scope SQL of spec 07-upsert-scope, inside the caller's transaction.</summary>
public interface IScopeRepository
{
    /// <summary>QUERY - Check existing scope: the non-DELETED scope FOR UPDATE.</summary>
    Task<bool> LockAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>QUERY - Check duplicate scope: another non-DELETED scope with the exact code.</summary>
    Task<bool> CodeTakenAsync(string scope, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check permission scope in use: a non-DELETED permission link to the scope.</summary>
    Task<bool> InUseAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>INSERT - Create scope; returns the generated id.</summary>
    Task<Guid> InsertAsync(ScopeValues values, string by, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update or delete scope, guarded by id and non-DELETED; false when no row.</summary>
    Task<bool> UpdateAsync(Guid id, ScopeValues values, string by, DateTime nowUtc, CancellationToken cancellationToken);
}
