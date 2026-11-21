using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the two scope queries of spec 09; both use the same predicates, no lock, no write.</summary>
internal sealed class ScopeQueryRepository(EcommerceDbContext db) : IScopeQueryRepository
{
    public Task<long> CountAsync(MasterFilter filter, CancellationToken cancellationToken) =>
        Matching(filter).LongCountAsync(cancellationToken);

    public async Task<IReadOnlyList<ScopeRow>> ListAsync(MasterFilter filter, long offset, long limit, CancellationToken cancellationToken) =>
        await Matching(filter)
            .OrderBy(s => s.Scope)
            .ThenBy(s => s.Id)
            .PageOf(offset, limit)
            .Select(s => new ScopeRow(s.Id, s.Scope, s.Name, s.Description, s.RowStatus))
            .ToListAsync(cancellationToken);

    /// <summary>Non-DELETED scopes; each null filter is disabled, search is one bound ILIKE pattern on the code or the description.</summary>
    private IQueryable<ScopeRecord> Matching(MasterFilter f) =>
        db.Scopes.Where(s => s.RowStatus != MasterLifecycle.Deleted
                             && (f.Id == null || s.Id == f.Id)
                             && (f.SearchPattern == null || EF.Functions.ILike(s.Scope, f.SearchPattern) || EF.Functions.ILike(s.Description, f.SearchPattern))
                             && (f.RowStatus == null || s.RowStatus == f.RowStatus));
}
