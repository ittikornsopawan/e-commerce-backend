using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for the two assignment queries of spec 11; the count and the page use the same joins and predicates; no lock, no write.</summary>
internal sealed class UserRoleQueryRepository(EcommerceDbContext db) : IUserRoleQueryRepository
{
    public Task<long> CountAsync(UserRoleFilter filter, CancellationToken cancellationToken) =>
        Matching(filter).LongCountAsync(cancellationToken);

    public async Task<IReadOnlyList<UserRoleListRow>> ListAsync(UserRoleFilter filter, long offset, long limit, CancellationToken cancellationToken) =>
        (await Matching(filter)
            .OrderBy(x => x.RoleName)
            .ThenBy(x => x.UserDisplayName)
            .ThenBy(x => x.Id)
            .PageOf(offset, limit)
            .ToListAsync(cancellationToken))
        .Select(x => new UserRoleListRow(x.Id, x.UserId, x.UserDisplayName, x.RoleId, x.RoleName, x.RowStatus))
        .ToList();

    /// <summary>
    /// Non-DELETED assignments joined to their user and role; each null filter is disabled. A member-initialised row, not the
    /// record, so EF Core can sort on its members in SQL.
    /// </summary>
    private IQueryable<Joined> Matching(UserRoleFilter f) =>
        from ur in db.UserRoles
        join u in db.Users on ur.UserId equals u.Id
        join r in db.Roles on ur.RoleId equals r.Id
        where ur.RowStatus != MasterLifecycle.Deleted
              && (f.Id == null || ur.Id == f.Id)
              && (f.UserId == null || ur.UserId == f.UserId)
              && (f.RoleId == null || ur.RoleId == f.RoleId)
              && (f.RowStatus == null || ur.RowStatus == f.RowStatus)
        select new Joined { Id = ur.Id, UserId = ur.UserId, UserDisplayName = u.DisplayName, RoleId = ur.RoleId, RoleName = r.Name, RowStatus = ur.RowStatus };

    private sealed class Joined
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public string? UserDisplayName { get; init; }
        public Guid RoleId { get; init; }
        public string RoleName { get; init; } = "";
        public string RowStatus { get; init; } = "";
    }
}
