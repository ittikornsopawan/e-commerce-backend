using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the user-role SQL of spec 10-upsert-user-role (Database Relations).</summary>
internal sealed class UserRoleRepository(EcommerceDbContext db) : IUserRoleRepository
{
    private const string Active = MasterLifecycle.Active;
    private const string Deleted = MasterLifecycle.Deleted;

    public async Task<IReadOnlyDictionary<Guid, string>> LockUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await db.Users
            .Where(u => userIds.Contains(u.Id))
            .OrderBy(u => u.Id)
            .ForUpdate()
            .Select(u => new { u.Id, u.RowStatus })
            .ToListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.RowStatus);

    public async Task<UserRoleRow?> LockAssignmentAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.UserRoles
            .Where(ur => ur.Id == id && ur.RowStatus != Deleted)
            .ForUpdate()
            .Select(ur => new UserRoleRow(ur.Id, ur.UserId, ur.RoleId, ur.RowStatus))
            .ToListAsync(cancellationToken)).SingleOrDefault();

    public Task<bool> TakenAsync(Guid userId, Guid roleId, Guid? excludeId, CancellationToken cancellationToken) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId && ur.RowStatus != Deleted && (excludeId == null || ur.Id != excludeId), cancellationToken);

    public async Task<Guid> InsertAsync(Guid userId, Guid roleId, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = new UserRoleRecord { CreatedBy = by, CreatedAt = nowUtc, RowStatus = rowStatus, UserId = userId, RoleId = roleId };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, string rowStatus, string by, DateTime nowUtc, CancellationToken cancellationToken) =>
        await db.UserRoles
            .Where(ur => ur.Id == id && ur.RowStatus != Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(ur => ur.UpdatedBy, by)
                .SetProperty(ur => ur.UpdatedAt, nowUtc)
                .SetProperty(ur => ur.RowStatus, rowStatus), cancellationToken) == 1;

    public Task<long> AdministratorsAsync(CancellationToken cancellationToken) =>
        (from ur in db.UserRoles
         join r in db.Roles on ur.RoleId equals r.Id
         join u in db.Users on ur.UserId equals u.Id
         where ur.RowStatus == Active && r.Name == RoleCode.PlatformAdmin && r.RowStatus == Active && u.RowStatus == Active
         select ur.Id).LongCountAsync(cancellationToken);
}
