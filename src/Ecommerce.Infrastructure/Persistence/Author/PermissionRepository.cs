using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the permission and scope-link SQL of specs 04-upsert-permission and 05-upsert-permission-batch (Database Relations).</summary>
internal sealed class PermissionRepository(EcommerceDbContext db) : IPermissionRepository
{
    private const string Deleted = MasterLifecycle.Deleted;

    public async Task<bool> LockAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Permissions
            .Where(p => p.Id == id && p.RowStatus != Deleted)
            .ForUpdate()
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)).Count == 1;

    public async Task<string?> LockAnyAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Permissions
            .Where(p => p.Id == id)
            .ForUpdate()
            .Select(p => p.RowStatus)
            .ToListAsync(cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlySet<Guid>> LockManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Permissions
            .Where(p => ids.Contains(p.Id) && p.RowStatus != Deleted)
            .OrderBy(p => p.Id)
            .ForUpdate()
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<IReadOnlySet<Guid>> LockActiveScopesAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken) =>
        (await db.Scopes
            .Where(s => scopeIds.Contains(s.Id) && s.RowStatus == MasterLifecycle.Active)
            .OrderBy(s => s.Id)
            .ForUpdate()
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<IReadOnlyDictionary<Guid, LinkRow>> LockLinksAsync(
        IReadOnlyCollection<Guid> permissionIds, IReadOnlyCollection<Guid> linkIds, CancellationToken cancellationToken) =>
        (await db.PermissionScopes
            .Where(ps => linkIds.Contains(ps.Id) && permissionIds.Contains(ps.PermissionId) && ps.RowStatus != Deleted)
            .OrderBy(ps => ps.Id)
            .ForUpdate()
            .Select(ps => new { ps.Id, ps.PermissionId, ps.ScopeId })
            .ToListAsync(cancellationToken)).ToDictionary(ps => ps.Id, ps => new LinkRow(ps.PermissionId, ps.ScopeId));

    public Task<long> CountLinksAsync(Guid permissionId, CancellationToken cancellationToken) =>
        db.PermissionScopes.LongCountAsync(ps => ps.PermissionId == permissionId && ps.RowStatus != Deleted, cancellationToken);

    public Task<bool> NameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken) =>
        db.Permissions.AnyAsync(p => p.Name == name && p.RowStatus != Deleted && (excludeId == null || p.Id != excludeId), cancellationToken);

    public async Task<bool> GrantedAsync(Guid permissionId, CancellationToken cancellationToken) =>
        await db.RolePermissions.AnyAsync(rp => rp.PermissionId == permissionId && rp.RowStatus != Deleted, cancellationToken)
        || await db.UserPermissions.AnyAsync(up => up.PermissionId == permissionId && up.RowStatus != Deleted, cancellationToken);

    public Task<bool> LinkTakenAsync(Guid permissionId, Guid scopeId, Guid? excludeLinkId, CancellationToken cancellationToken) =>
        db.PermissionScopes.AnyAsync(ps => ps.PermissionId == permissionId
                                           && ps.ScopeId == scopeId
                                           && ps.RowStatus != Deleted
                                           && (excludeLinkId == null || ps.Id != excludeLinkId), cancellationToken);

    public async Task<bool> LinkInUseAsync(Guid linkId, CancellationToken cancellationToken) =>
        await db.RolePermissionScopes.AnyAsync(rps => rps.PermissionScopeId == linkId && rps.RowStatus != Deleted, cancellationToken)
        || await db.UserPermissions.AnyAsync(up => up.PermissionScopeId == linkId && up.RowStatus != Deleted, cancellationToken);

    public async Task<Guid> InsertAsync(PermissionValues values, string by, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = new PermissionRecord
        {
            CreatedBy = by,
            CreatedAt = nowUtc,
            RowStatus = values.RowStatus,
            Name = values.Name,
            Description = values.Description
        };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, PermissionValues values, string by, DateTime nowUtc, CancellationToken cancellationToken) =>
        await db.Permissions
            .Where(p => p.Id == id && p.RowStatus != Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.UpdatedBy, by)
                .SetProperty(p => p.UpdatedAt, nowUtc)
                .SetProperty(p => p.RowStatus, values.RowStatus)
                .SetProperty(p => p.Name, values.Name)
                .SetProperty(p => p.Description, values.Description), cancellationToken) == 1;

    public async Task<Guid> InsertLinkAsync(Guid permissionId, Guid scopeId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = new PermissionScopeRecord
        {
            CreatedBy = by,
            CreatedAt = nowUtc,
            RowStatus = rowStatus,
            PermissionId = permissionId,
            ScopeId = scopeId
        };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> UpdateLinkAsync(
        Guid linkId, Guid permissionId, Guid scopeId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken) =>
        await db.PermissionScopes
            .Where(ps => ps.Id == linkId && ps.PermissionId == permissionId && ps.ScopeId == scopeId && ps.RowStatus != Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ps => ps.UpdatedBy, by)
                .SetProperty(ps => ps.UpdatedAt, nowUtc)
                .SetProperty(ps => ps.RowStatus, rowStatus), cancellationToken) == 1;
}
