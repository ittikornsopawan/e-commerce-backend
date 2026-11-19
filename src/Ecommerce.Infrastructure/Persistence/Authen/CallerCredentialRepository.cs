using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for "Lock the user" and "Check the current access token" (specs 07 to 10).</summary>
internal sealed class CallerCredentialRepository(EcommerceDbContext db) : ICallerCredentialRepository
{
    public async Task<bool> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.Users.Where(u => u.Id == userId && u.RowStatus == "ACTIVE").ForUpdate().Select(u => u.Id).ToListAsync(cancellationToken)).Count == 1;

    public Task<bool> IsCurrentAccessTokenAsync(byte[] accessTokenHash, Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        db.AccessTokens.AnyAsync(t => t.Token == accessTokenHash && t.UserId == userId && t.RowStatus == "ACTIVE" && t.ExpiredAt > nowUtc, cancellationToken);
}
