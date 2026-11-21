namespace Ecommerce.Domain.Author.Repositories;

/// <summary>One non-DELETED role as spec 03 returns it.</summary>
public sealed record RoleRow(Guid Id, string Name, string Description, string RowStatus);

/// <summary>The role reads of spec 03-inquiry-role, in the caller's read-only snapshot; no lock, no write.</summary>
public interface IRoleQueryRepository
{
    /// <summary>QUERY - Get total items.</summary>
    Task<long> CountAsync(MasterFilter filter, CancellationToken cancellationToken);

    /// <summary>QUERY - Get roles: name, then id.</summary>
    Task<IReadOnlyList<RoleRow>> ListAsync(MasterFilter filter, long offset, long limit, CancellationToken cancellationToken);
}
