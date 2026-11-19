using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>
/// Closes the ACTIVE token rows of one session of one user (specs 05 reuse and 06 logout, "Close the ... tokens of the
/// session"). Both guard user_id, so legacy data that reuses a session id for another user is never touched.
/// </summary>
internal static class SessionRevocation
{
    public static Task<int> CloseAccessTokensAsync(EcommerceDbContext db, Guid sessionId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var by = userId.ToString();
        return db.AccessTokens.Where(t => t.SessionId == sessionId && t.UserId == userId && t.RowStatus == "ACTIVE")
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedBy, by).SetProperty(t => t.UpdatedAt, nowUtc).SetProperty(t => t.RowStatus, "INACTIVE"), cancellationToken);
    }

    public static Task<int> CloseRefreshTokensAsync(EcommerceDbContext db, Guid sessionId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var by = userId.ToString();
        return db.RefreshTokens.Where(t => t.SessionId == sessionId && t.UserId == userId && t.RowStatus == "ACTIVE")
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedBy, by).SetProperty(t => t.UpdatedAt, nowUtc).SetProperty(t => t.RowStatus, "INACTIVE"), cancellationToken);
    }
}
