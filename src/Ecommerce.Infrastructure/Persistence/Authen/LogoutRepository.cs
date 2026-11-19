using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for the SQL of spec 06-logout (Database Relations).</summary>
internal sealed class LogoutRepository(EcommerceDbContext db) : ILogoutRepository
{
    public async Task<bool> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.Users.Where(u => u.Id == userId && u.RowStatus == "ACTIVE").ForUpdate().Select(u => u.Id).ToListAsync(cancellationToken)).Count == 1;

    public async Task<Guid?> GetSessionAsync(byte[] accessTokenHash, Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
        (await db.AccessTokens.Where(t => t.Token == accessTokenHash && t.UserId == userId && t.RowStatus == "ACTIVE" && t.ExpiredAt > nowUtc)
            .Select(t => (Guid?)t.SessionId).ToListAsync(cancellationToken)).SingleOrDefault();

    public async Task<int> CloseSessionAsync(Guid sessionId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var access = await SessionRevocation.CloseAccessTokensAsync(db, sessionId, userId, nowUtc, cancellationToken);
        await SessionRevocation.CloseRefreshTokensAsync(db, sessionId, userId, nowUtc, cancellationToken);
        return access;
    }
}
