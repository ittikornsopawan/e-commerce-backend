using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the direct-grant SQL of spec 15-upsert-user-permission (Database Relations).</summary>
internal sealed class UserPermissionRepository(EcommerceDbContext db) : IUserPermissionRepository
{
    private const string Deleted = MasterLifecycle.Deleted;

    public async Task<UserPermissionRow?> LockGrantAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.UserPermissions
            .Where(up => up.Id == id && up.RowStatus != Deleted)
            .ForUpdate()
            .Select(up => new UserPermissionRow(up.Id, up.UserId, up.PermissionId, up.PermissionScopeId, up.RowStatus))
            .ToListAsync(cancellationToken)).SingleOrDefault();

    // "IS NOT DISTINCT FROM": EF Core compares a nullable column to a nullable parameter with NULL matching NULL.
    public Task<bool> TakenAsync(Guid userId, Guid permissionId, Guid? permissionScopeId, Guid? excludeId, CancellationToken cancellationToken) =>
        db.UserPermissions.AnyAsync(up => up.UserId == userId
                                          && up.PermissionId == permissionId
                                          && up.PermissionScopeId == permissionScopeId
                                          && up.RowStatus != Deleted
                                          && (excludeId == null || up.Id != excludeId), cancellationToken);

    public async Task<Guid> InsertAsync(
        Guid userId, Guid permissionId, Guid? permissionScopeId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = new UserPermissionRecord
        {
            CreatedBy = by,
            CreatedAt = nowUtc,
            RowStatus = rowStatus,
            UserId = userId,
            PermissionId = permissionId,
            PermissionScopeId = permissionScopeId
        };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken) =>
        await db.UserPermissions
            .Where(up => up.Id == id && up.RowStatus != Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(up => up.UpdatedBy, by)
                .SetProperty(up => up.UpdatedAt, nowUtc)
                .SetProperty(up => up.RowStatus, rowStatus), cancellationToken) == 1;
}
