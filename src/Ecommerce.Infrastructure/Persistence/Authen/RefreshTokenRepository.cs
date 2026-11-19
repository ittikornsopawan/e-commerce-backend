using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for the refresh-specific SQL of spec 05-refresh-token (Database Relations).</summary>
internal sealed class RefreshTokenRepository(EcommerceDbContext db, FieldProtector protector) : IRefreshTokenRepository
{
    public Task<RefreshOwner?> FindOwnerAsync(byte[] tokenHash, DateTime lookupNowUtc, CancellationToken cancellationToken) =>
        db.RefreshTokens.Where(t => t.Token == tokenHash && t.ExpiredAt > lookupNowUtc)
            .Select(t => new RefreshOwner(t.Id, t.UserId)).FirstOrDefaultAsync(cancellationToken);

    public async Task<LoginUser?> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.Users.Where(u => u.Id == userId && u.RowStatus == "ACTIVE").ForUpdate()
            .Select(u => new { u.Id, u.Username, u.EmailAddress, u.DisplayName }).ToListAsync(cancellationToken);
        return rows.Count == 1
            ? new LoginUser(rows[0].Id, protector.Unprotect(rows[0].Username), protector.Unprotect(rows[0].EmailAddress), rows[0].DisplayName)
            : null;
    }

    public Task<RefreshTokenRow?> RereadAsync(RefreshOwner owner, byte[] tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.Where(t => t.Id == owner.RefreshTokenId && t.UserId == owner.UserId && t.Token == tokenHash)
            .Select(t => new RefreshTokenRow(t.Id, t.UserId, t.SessionId, t.RowStatus, t.UpdatedAt, t.ExpiredAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> ConsumeAsync(RefreshTokenRow row, byte[] tokenHash, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var by = row.UserId.ToString();
        return await db.RefreshTokens
            .Where(t => t.Id == row.Id && t.UserId == row.UserId && t.SessionId == row.SessionId && t.Token == tokenHash && t.RowStatus == "ACTIVE" && t.ExpiredAt > nowUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedBy, by).SetProperty(t => t.UpdatedAt, nowUtc).SetProperty(t => t.RowStatus, "INACTIVE"), cancellationToken) == 1;
    }

    public async Task CloseSessionAsync(Guid sessionId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await SessionRevocation.CloseRefreshTokensAsync(db, sessionId, userId, nowUtc, cancellationToken);
        await SessionRevocation.CloseAccessTokensAsync(db, sessionId, userId, nowUtc, cancellationToken);
    }
}
