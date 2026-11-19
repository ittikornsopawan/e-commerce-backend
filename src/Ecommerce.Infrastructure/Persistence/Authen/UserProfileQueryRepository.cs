using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for the SQL of spec 08-inquiry-user-profile; no lock, no write.</summary>
internal sealed class UserProfileQueryRepository(EcommerceDbContext db, FieldProtector protector) : IUserProfileQueryRepository
{
    public async Task<ProfileUser?> GetUserAsync(Guid userId, byte[] accessTokenHash, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = await db.Users
            .Join(db.AccessTokens, u => u.Id, t => t.UserId, (u, t) => new { u, t })
            .Where(x => x.u.Id == userId && x.u.RowStatus == "ACTIVE"
                        && x.t.Token == accessTokenHash && x.t.UserId == userId && x.t.RowStatus == "ACTIVE" && x.t.ExpiredAt > nowUtc)
            .Select(x => new { x.u.EmailAddress, x.u.FirstName, x.u.LastName, x.u.DisplayName, x.u.ProfilePicturePath })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ProfileUser(protector.Unprotect(row.EmailAddress), Unprotect(row.FirstName), Unprotect(row.LastName), row.DisplayName, row.ProfilePicturePath);
    }

    public async Task<ProfileInformation?> GetInformationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await db.UserInformation.Where(i => i.UserId == userId && i.RowStatus != "DELETED")
            .Select(i => new { i.PhoneNumber, i.Address, i.DateOfBirth, i.Gender })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ProfileInformation(Unprotect(row.PhoneNumber), Unprotect(row.Address), row.DateOfBirth, row.Gender == "" ? null : row.Gender);
    }

    private string? Unprotect(byte[]? value) => value is null ? null : protector.Unprotect(value);
}
