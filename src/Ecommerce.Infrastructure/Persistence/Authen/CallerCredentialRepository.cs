using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for "Lock the user", "Check the current access token" (specs 07, 09) and "Check current credential and user" (spec 10).</summary>
internal sealed class CallerCredentialRepository(EcommerceDbContext db) : ICallerCredentialRepository
{
    public async Task<bool> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.Users.Where(u => u.Id == userId && u.RowStatus == "ACTIVE").ForUpdate().Select(u => u.Id).ToListAsync(cancellationToken)).Count == 1;

    public Task<bool> IsCurrentAccessTokenAsync(byte[] accessTokenHash, Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        db.AccessTokens.AnyAsync(t => t.Token == accessTokenHash && t.UserId == userId && t.RowStatus == "ACTIVE" && t.ExpiredAt > nowUtc, cancellationToken);

    public Task<bool> IsCurrentCredentialAsync(byte[] accessTokenHash, Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        db.AccessTokens
            .Join(db.Users, t => t.UserId, u => u.Id, (t, u) => new { t, u })
            .AnyAsync(x => x.t.Token == accessTokenHash && x.t.UserId == userId && x.t.RowStatus == "ACTIVE" && x.t.ExpiredAt > nowUtc
                           && x.u.RowStatus == "ACTIVE", cancellationToken);
}
