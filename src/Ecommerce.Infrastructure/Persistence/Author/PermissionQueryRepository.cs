using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the four permission queries of spec 06; the count and the page use the same predicates; no lock, no write.</summary>
internal sealed class PermissionQueryRepository(EcommerceDbContext db) : IPermissionQueryRepository
{
    public Task<long> CountAsync(MasterFilter filter, CancellationToken cancellationToken) =>
        Matching(filter).LongCountAsync(cancellationToken);

    public async Task<IReadOnlyList<PermissionRow>> ListAsync(MasterFilter filter, long offset, long limit, CancellationToken cancellationToken) =>
        await Matching(filter)
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .PageOf(offset, limit)
            .Select(p => new PermissionRow(p.Id, p.Name, p.Description, p.RowStatus))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PermissionLinkRow>> ListLinksAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken) =>
        await db.PermissionScopes
            .Where(ps => permissionIds.Contains(ps.PermissionId) && ps.RowStatus != MasterLifecycle.Deleted)
            .OrderBy(ps => ps.PermissionId)
            .ThenBy(ps => ps.Id)
            .Select(ps => new PermissionLinkRow(ps.Id, ps.PermissionId, ps.ScopeId, ps.RowStatus))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScopeMasterRow>> ListScopesAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken) =>
        await db.Scopes
            .Where(s => scopeIds.Contains(s.Id))
            .OrderBy(s => s.Scope)
            .ThenBy(s => s.Id)
            .Select(s => new ScopeMasterRow(s.Id, s.Scope, s.Name))
            .ToListAsync(cancellationToken);

    /// <summary>Non-DELETED permissions; each null filter is disabled, search is one bound ILIKE pattern on name or description.</summary>
    private IQueryable<PermissionRecord> Matching(MasterFilter f) =>
        db.Permissions.Where(p => p.RowStatus != MasterLifecycle.Deleted
                                  && (f.Id == null || p.Id == f.Id)
                                  && (f.SearchPattern == null || EF.Functions.ILike(p.Name, f.SearchPattern) || EF.Functions.ILike(p.Description, f.SearchPattern))
                                  && (f.RowStatus == null || p.RowStatus == f.RowStatus));
}
