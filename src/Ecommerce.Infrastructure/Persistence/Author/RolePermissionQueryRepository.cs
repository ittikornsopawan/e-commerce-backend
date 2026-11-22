using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the three grant queries of spec 14; the count and the page use the same joins and predicates; no lock, no write.</summary>
internal sealed class RolePermissionQueryRepository(EcommerceDbContext db) : IRolePermissionQueryRepository
{
    private const string Deleted = MasterLifecycle.Deleted;

    public Task<long> CountAsync(RolePermissionFilter filter, CancellationToken cancellationToken) =>
        Matching(filter).LongCountAsync(cancellationToken);

    public async Task<IReadOnlyList<RolePermissionListRow>> ListAsync(RolePermissionFilter filter, long offset, long limit, CancellationToken cancellationToken) =>
        (await Matching(filter)
            .OrderBy(x => x.RoleName)
            .ThenBy(x => x.PermissionName)
            .ThenBy(x => x.Id)
            .PageOf(offset, limit)
            .ToListAsync(cancellationToken))
        .Select(x => new RolePermissionListRow(x.Id, x.RoleId, x.RoleName, x.PermissionId, x.PermissionName, x.RowStatus))
        .ToList();

    public async Task<IReadOnlyList<RolePermissionScopeListRow>> ListScopesAsync(IReadOnlyCollection<Guid> grantIds, CancellationToken cancellationToken) =>
        (await (from rps in db.RolePermissionScopes
                join ps in db.PermissionScopes on rps.PermissionScopeId equals ps.Id
                join s in db.Scopes on ps.ScopeId equals s.Id
                where grantIds.Contains(rps.RolePermissionId) && rps.RowStatus != Deleted
                orderby rps.RolePermissionId, s.Scope, rps.Id
                select new { rps.Id, rps.RolePermissionId, rps.PermissionScopeId, ps.ScopeId, s.Scope, rps.RowStatus }).ToListAsync(cancellationToken))
        .Select(x => new RolePermissionScopeListRow(x.Id, x.RolePermissionId, x.PermissionScopeId, x.ScopeId, x.Scope, x.RowStatus))
        .ToList();

    /// <summary>Non-DELETED grants joined to their role and permission; each null filter is disabled. A member-initialised row so EF Core sorts in SQL.</summary>
    private IQueryable<Joined> Matching(RolePermissionFilter f) =>
        from rp in db.RolePermissions
        join r in db.Roles on rp.RoleId equals r.Id
        join p in db.Permissions on rp.PermissionId equals p.Id
        where rp.RowStatus != Deleted
              && (f.Id == null || rp.Id == f.Id)
              && (f.RoleId == null || rp.RoleId == f.RoleId)
              && (f.PermissionId == null || rp.PermissionId == f.PermissionId)
              && (f.RowStatus == null || rp.RowStatus == f.RowStatus)
        select new Joined { Id = rp.Id, RoleId = rp.RoleId, RoleName = r.Name, PermissionId = rp.PermissionId, PermissionName = p.Name, RowStatus = rp.RowStatus };

    private sealed class Joined
    {
        public Guid Id { get; init; }
        public Guid RoleId { get; init; }
        public string RoleName { get; init; } = "";
        public Guid PermissionId { get; init; }
        public string PermissionName { get; init; } = "";
        public string RowStatus { get; init; } = "";
    }
}
