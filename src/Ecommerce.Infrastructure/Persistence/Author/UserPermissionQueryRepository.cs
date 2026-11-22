using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the two direct-grant queries of spec 16; the count and the page use the same filters; no lock, no write.</summary>
internal sealed class UserPermissionQueryRepository(EcommerceDbContext db) : IUserPermissionQueryRepository
{
    public Task<long> CountAsync(UserPermissionFilter filter, CancellationToken cancellationToken) =>
        Matching(filter).LongCountAsync(cancellationToken);

    // PostgreSQL sorts NULL last in ascending order, so a grant without a scope comes after the scoped ones.
    public async Task<IReadOnlyList<UserPermissionListRow>> ListAsync(UserPermissionFilter filter, long offset, long limit, CancellationToken cancellationToken) =>
        (await Matching(filter)
            .OrderBy(x => x.PermissionName)
            .ThenBy(x => x.UserDisplayName)
            .ThenBy(x => x.Scope)
            .ThenBy(x => x.Id)
            .PageOf(offset, limit)
            .ToListAsync(cancellationToken))
        .Select(x => new UserPermissionListRow(x.Id, x.UserId, x.UserDisplayName, x.PermissionId, x.PermissionName, x.PermissionScopeId, x.ScopeId, x.Scope, x.RowStatus))
        .ToList();

    /// <summary>
    /// Non-DELETED grants joined to their user and permission, left-joined to the scope definition and the scope master; each null
    /// filter is disabled. A member-initialised row, not the record, so EF Core can sort on its members in SQL.
    /// </summary>
    private IQueryable<Joined> Matching(UserPermissionFilter f) =>
        from up in db.UserPermissions
        join u in db.Users on up.UserId equals u.Id
        join p in db.Permissions on up.PermissionId equals p.Id
        from ps in db.PermissionScopes.Where(ps => ps.Id == up.PermissionScopeId).DefaultIfEmpty()
        from s in db.Scopes.Where(s => ps != null && s.Id == ps.ScopeId).DefaultIfEmpty()
        where up.RowStatus != MasterLifecycle.Deleted
              && (f.Id == null || up.Id == f.Id)
              && (f.UserId == null || up.UserId == f.UserId)
              && (f.PermissionId == null || up.PermissionId == f.PermissionId)
              && (f.RowStatus == null || up.RowStatus == f.RowStatus)
        select new Joined
        {
            Id = up.Id,
            UserId = up.UserId,
            UserDisplayName = u.DisplayName,
            PermissionId = up.PermissionId,
            PermissionName = p.Name,
            PermissionScopeId = up.PermissionScopeId,
            ScopeId = ps == null ? null : ps.ScopeId,
            Scope = s == null ? null : s.Scope,
            RowStatus = up.RowStatus
        };

    private sealed class Joined
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string? UserDisplayName { get; init; }
        public Guid PermissionId { get; init; }
        public string PermissionName { get; init; } = "";
        public Guid? PermissionScopeId { get; init; }
        public Guid? ScopeId { get; init; }
        public string? Scope { get; init; }
        public string RowStatus { get; init; } = "";
    }
}
