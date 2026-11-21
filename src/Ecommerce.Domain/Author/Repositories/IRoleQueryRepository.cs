namespace Ecommerce.Domain.Author.Repositories;

/// <summary>The identical predicates of the count and the page; a null filter is disabled (typed SQL NULL).</summary>
public sealed record RoleFilter(Guid? Id, string? SearchPattern, string? RowStatus);

/// <summary>One non-DELETED role as spec 03 returns it.</summary>
public sealed record RoleRow(Guid Id, string Name, string Description, string RowStatus);

/// <summary>The role reads of spec 03-inquiry-role, in the caller's read-only snapshot; no lock, no write.</summary>
public interface IRoleQueryRepository
{
    /// <summary>QUERY - Get total items.</summary>
    Task<long> CountAsync(RoleFilter filter, CancellationToken cancellationToken);

    /// <summary>QUERY - Get roles: name, then id.</summary>
    Task<IReadOnlyList<RoleRow>> ListAsync(RoleFilter filter, long offset, long limit, CancellationToken cancellationToken);
}
