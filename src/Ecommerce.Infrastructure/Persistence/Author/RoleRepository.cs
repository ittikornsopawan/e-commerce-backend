using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the role SQL of specs 01-upsert-role and 02-upsert-role-batch (Database Relations).</summary>
internal sealed class RoleRepository(EcommerceDbContext db) : IRoleRepository
{
    private const string Deleted = MasterLifecycle.Deleted;

    public async Task<string?> LockNameAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Roles
            .Where(r => r.Id == id && r.RowStatus != Deleted)
            .ForUpdate()
            .Select(r => r.Name)
            .ToListAsync(cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyDictionary<Guid, string>> LockNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        // One statement locks every target in id order, whatever the request order, so two batches never deadlock.
        var locked = await db.Roles
            .Where(r => ids.Contains(r.Id) && r.RowStatus != Deleted)
            .OrderBy(r => r.Id)
            .ForUpdate()
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(cancellationToken);

        return locked.ToDictionary(r => r.Id, r => r.Name);
    }

    public async Task<LockedRole?> LockAnyAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Roles
            .Where(r => r.Id == id)
            .ForUpdate()
            .Select(r => new LockedRole(r.Name, r.RowStatus))
            .ToListAsync(cancellationToken)).SingleOrDefault();

    public Task<bool> NameTakenAsync(string name, Guid? excludeId, CancellationToken cancellationToken) =>
        db.Roles.AnyAsync(r => r.Name == name && r.RowStatus != Deleted && (excludeId == null || r.Id != excludeId), cancellationToken);

    public async Task<bool> InUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.UserRoles.AnyAsync(ur => ur.RoleId == id && ur.RowStatus != Deleted, cancellationToken)
        || await db.RolePermissions.AnyAsync(rp => rp.RoleId == id && rp.RowStatus != Deleted, cancellationToken);

    public async Task<Guid> InsertAsync(RoleValues values, string by, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = new RoleRecord
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

    public async Task<bool> UpdateAsync(Guid id, RoleValues values, string by, DateTime nowUtc, CancellationToken cancellationToken) =>
        await db.Roles
            .Where(r => r.Id == id && r.RowStatus != Deleted && r.Name != RoleCode.PlatformAdmin)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.UpdatedBy, by)
                .SetProperty(r => r.UpdatedAt, nowUtc)
                .SetProperty(r => r.RowStatus, values.RowStatus)
                .SetProperty(r => r.Name, values.Name)
                .SetProperty(r => r.Description, values.Description), cancellationToken) == 1;
}
