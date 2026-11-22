namespace Ecommerce.Domain.Author.Repositories;

/// <summary>One non-DELETED scope as spec 09 returns it.</summary>
public sealed record ScopeRow(Guid Id, string Scope, string Name, string Description, string RowStatus);

/// <summary>The scope reads of spec 09-inquiry-scope, in the caller's read-only snapshot; no lock, no write.</summary>
public interface IScopeQueryRepository
{
    /// <summary>QUERY - Get total items; search matches the scope code or the description.</summary>
    Task<long> CountAsync(MasterFilter filter, CancellationToken cancellationToken);

    /// <summary>QUERY - Get scopes: scope code, then id.</summary>
    Task<IReadOnlyList<ScopeRow>> ListAsync(MasterFilter filter, long offset, long limit, CancellationToken cancellationToken);
}
