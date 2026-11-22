using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Author;

/// <summary>EF Core LINQ for "QUERY - Lock the users" of specs 10 and 15: one statement, ascending id, so assignment writers never deadlock.</summary>
internal sealed class UserLockRepository(EcommerceDbContext db) : IUserLockRepository
{
    public async Task<IReadOnlyDictionary<Guid, string>> LockUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await db.Users
            .Where(u => userIds.Contains(u.Id))
            .OrderBy(u => u.Id)
            .ForUpdate()
            .Select(u => new { u.Id, u.RowStatus })
            .ToListAsync(cancellationToken)).ToDictionary(u => u.Id, u => u.RowStatus);
}
