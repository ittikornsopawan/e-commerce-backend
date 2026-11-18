using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for the SQL of spec 03-reset-password (Database Relations).</summary>
internal sealed class PasswordResetRequestRepository(EcommerceDbContext db, FieldProtector protector) : IPasswordResetRequestRepository
{
    public async Task<Guid?> FindActiveUserIdAsync(string emailAddress, CancellationToken cancellationToken)
    {
        var bytes = protector.Protect(emailAddress);
        var ids = await db.Users.Where(u => u.EmailAddress == bytes && u.RowStatus == "ACTIVE").Select(u => u.Id).ToListAsync(cancellationToken);
        return ids.Count == 1 ? ids[0] : null;
    }

    public async Task<bool> LockActiveUserAsync(Guid userId, string emailAddress, CancellationToken cancellationToken)
    {
        var bytes = protector.Protect(emailAddress);
        return (await db.Users.Where(u => u.Id == userId && u.RowStatus == "ACTIVE" && u.EmailAddress == bytes)
            .ForUpdate().Select(u => u.Id).ToListAsync(cancellationToken)).Count == 1;
    }

    public Task<int> CountRequestsAsync(Guid userId, DateTime windowStartUtc, CancellationToken cancellationToken) =>
        db.PasswordResets.CountAsync(r => r.UserId == userId && r.CreatedAt > windowStartUtc, cancellationToken);

    public async Task<Guid> ReplaceActiveResetAsync(
        Guid userId, byte[] resetTokenHash, DateTime nowUtc, DateTime expiredAtUtc, CancellationToken cancellationToken)
    {
        var by = userId.ToString();
        await db.PasswordResets.Where(r => r.UserId == userId && r.RowStatus == "ACTIVE")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UpdatedBy, by).SetProperty(r => r.UpdatedAt, nowUtc).SetProperty(r => r.RowStatus, "INACTIVE"), cancellationToken);

        var row = new PasswordResetRecord { CreatedBy = by, CreatedAt = nowUtc, UserId = userId, ResetToken = resetTokenHash, ExpiredAt = expiredAtUtc };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }
}
