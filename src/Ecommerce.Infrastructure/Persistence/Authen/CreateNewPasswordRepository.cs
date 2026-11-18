using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for the SQL of spec 04-create-new-password (Database Relations).</summary>
internal sealed class CreateNewPasswordRepository(EcommerceDbContext db) : ICreateNewPasswordRepository
{
    public Task<ResetRoute?> FindResetAsync(byte[] resetTokenHash, DateTime lookupNowUtc, CancellationToken cancellationToken) =>
        db.PasswordResets
            .Where(r => r.ResetToken == resetTokenHash && r.RowStatus == "ACTIVE" && r.ExpiredAt > lookupNowUtc)
            .Select(r => new ResetRoute(r.Id, r.UserId))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> LockActiveUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.Users.Where(u => u.Id == userId && u.RowStatus == "ACTIVE").ForUpdate().Select(u => u.Id).ToListAsync(cancellationToken)).Count == 1;

    public async Task<bool> ConsumeResetAsync(ResetRoute route, byte[] resetTokenHash, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var by = route.UserId.ToString();
        var consumed = await db.PasswordResets
            .Where(r => r.Id == route.ResetId && r.UserId == route.UserId && r.ResetToken == resetTokenHash && r.RowStatus == "ACTIVE" && r.ExpiredAt > nowUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UpdatedBy, by).SetProperty(r => r.UpdatedAt, nowUtc).SetProperty(r => r.RowStatus, "INACTIVE"), cancellationToken);
        return consumed == 1;
    }

    public Task<CurrentPassword?> GetCurrentPasswordAsync(Guid userId, CancellationToken cancellationToken) =>
        db.UserPasswords
            .Where(p => p.UserId == userId && p.RowType == "PASSWORD" && p.RowStatus == "ACTIVE")
            .Select(p => new CurrentPassword(p.Id, new PasswordCredential(p.Password, p.PasswordSalt)))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> ReplacePasswordAsync(
        Guid userId, Guid currentPasswordId, PasswordCredential newPassword, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var by = userId.ToString();
        var closed = await db.UserPasswords
            .Where(p => p.Id == currentPasswordId && p.UserId == userId && p.RowType == "PASSWORD" && p.RowStatus == "ACTIVE")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedBy, by).SetProperty(p => p.UpdatedAt, nowUtc).SetProperty(p => p.RowStatus, "INACTIVE"), cancellationToken);
        if (closed != 1) return false;

        db.Add(new UserPasswordRecord { CreatedBy = by, RowType = "PASSWORD", UserId = userId, Password = newPassword.Hash, PasswordSalt = newPassword.Salt });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task CloseAllTokensAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var by = userId.ToString();
        // Every session, expired ACTIVE rows included; never filtered by session, token or expiry.
        await db.AccessTokens.Where(t => t.UserId == userId && t.RowStatus == "ACTIVE")
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedBy, by).SetProperty(t => t.UpdatedAt, nowUtc).SetProperty(t => t.RowStatus, "INACTIVE"), cancellationToken);
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RowStatus == "ACTIVE")
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedBy, by).SetProperty(t => t.UpdatedAt, nowUtc).SetProperty(t => t.RowStatus, "INACTIVE"), cancellationToken);
    }
}
