using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the two role queries of spec 03; both use the same predicates, no lock, no write.</summary>
internal sealed class RoleQueryRepository(EcommerceDbContext db) : IRoleQueryRepository
{
    public Task<long> CountAsync(MasterFilter filter, CancellationToken cancellationToken) =>
        Matching(filter).LongCountAsync(cancellationToken);

    public async Task<IReadOnlyList<RoleRow>> ListAsync(MasterFilter filter, long offset, long limit, CancellationToken cancellationToken) =>
        await Matching(filter)
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id)
            .PageOf(offset, limit)
            .Select(r => new RoleRow(r.Id, r.Name, r.Description, r.RowStatus))
            .ToListAsync(cancellationToken);

    /// <summary>Non-DELETED roles; each null filter is disabled, search is one bound ILIKE pattern on name or description.</summary>
    private IQueryable<RoleRecord> Matching(MasterFilter f) =>
        db.Roles.Where(r => r.RowStatus != MasterLifecycle.Deleted
                            && (f.Id == null || r.Id == f.Id)
                            && (f.SearchPattern == null || EF.Functions.ILike(r.Name, f.SearchPattern) || EF.Functions.ILike(r.Description, f.SearchPattern))
                            && (f.RowStatus == null || r.RowStatus == f.RowStatus));
}
