using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the grant SQL of spec 13-upsert-role-permission (Database Relations).</summary>
internal sealed class RolePermissionRepository(EcommerceDbContext db) : IRolePermissionRepository
{
    private const string Deleted = MasterLifecycle.Deleted;

    public async Task<RolePermissionRow?> LockGrantAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.RolePermissions
            .Where(rp => rp.Id == id && rp.RowStatus != Deleted)
            .ForUpdate()
            .Select(rp => new RolePermissionRow(rp.Id, rp.RoleId, rp.PermissionId, rp.RowStatus))
            .ToListAsync(cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<GrantScopeRow>> LockScopesAsync(Guid grantId, CancellationToken cancellationToken) =>
        await db.RolePermissionScopes
            .Where(rps => rps.RolePermissionId == grantId && rps.RowStatus != Deleted)
            .OrderBy(rps => rps.Id)
            .ForUpdate()
            .Select(rps => new GrantScopeRow(rps.Id, rps.PermissionScopeId, rps.RowStatus))
            .ToListAsync(cancellationToken);

    public Task<bool> TakenAsync(Guid roleId, Guid permissionId, Guid? excludeId, CancellationToken cancellationToken) =>
        db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId && rp.RowStatus != Deleted && (excludeId == null || rp.Id != excludeId), cancellationToken);

    public async Task<Guid> InsertAsync(Guid roleId, Guid permissionId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = new RolePermissionRecord { CreatedBy = by, CreatedAt = nowUtc, RowStatus = rowStatus, RoleId = roleId, PermissionId = permissionId };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken) =>
        await db.RolePermissions
            .Where(rp => rp.Id == id && rp.RowStatus != Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rp => rp.UpdatedBy, by)
                .SetProperty(rp => rp.UpdatedAt, nowUtc)
                .SetProperty(rp => rp.RowStatus, rowStatus), cancellationToken) == 1;

    public async Task<Guid> InsertScopeAsync(Guid grantId, Guid permissionScopeId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = new RolePermissionScopeRecord { CreatedBy = by, CreatedAt = nowUtc, RowStatus = rowStatus, RolePermissionId = grantId, PermissionScopeId = permissionScopeId };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> UpdateScopeAsync(Guid id, Guid grantId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken) =>
        await db.RolePermissionScopes
            .Where(rps => rps.Id == id && rps.RolePermissionId == grantId && rps.RowStatus != Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rps => rps.UpdatedBy, by)
                .SetProperty(rps => rps.UpdatedAt, nowUtc)
                .SetProperty(rps => rps.RowStatus, rowStatus), cancellationToken) == 1;

    public Task DeleteScopesAsync(Guid grantId, string by, DateTime nowUtc, CancellationToken cancellationToken) =>
        db.RolePermissionScopes
            .Where(rps => rps.RolePermissionId == grantId && rps.RowStatus != Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(rps => rps.UpdatedBy, by)
                .SetProperty(rps => rps.UpdatedAt, nowUtc)
                .SetProperty(rps => rps.RowStatus, Deleted), cancellationToken);

    public Task<long> ActiveScopesAsync(Guid grantId, CancellationToken cancellationToken) =>
        db.RolePermissionScopes.LongCountAsync(rps => rps.RolePermissionId == grantId && rps.RowStatus == MasterLifecycle.Active, cancellationToken);
}
