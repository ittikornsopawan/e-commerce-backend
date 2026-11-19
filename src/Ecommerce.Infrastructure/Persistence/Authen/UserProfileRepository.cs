using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>
/// EF Core LINQ for the profile SQL of spec 07. The spec's <c>CASE WHEN has&lt;Field&gt;</c> becomes "set only the
/// supplied properties"; null clears a field, an omitted field is not touched.
/// </summary>
internal sealed class UserProfileRepository(EcommerceDbContext db, FieldProtector protector) : IUserProfileRepository
{
    public async Task<bool> UpdateUserAsync(Guid userId, ProfileUserChanges c, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var by = userId.ToString();
        var firstName = Protect(c.FirstName.Value);
        var lastName = Protect(c.LastName.Value);
        return await db.Users.Where(u => u.Id == userId && u.RowStatus == "ACTIVE").ExecuteUpdateAsync(s =>
        {
            s.SetProperty(u => u.UpdatedBy, by).SetProperty(u => u.UpdatedAt, nowUtc);
            if (c.FirstName.IsSet) s.SetProperty(u => u.FirstName, firstName);
            if (c.LastName.IsSet) s.SetProperty(u => u.LastName, lastName);
            if (c.DisplayName.IsSet) s.SetProperty(u => u.DisplayName, c.DisplayName.Value);
            if (c.ProfilePicturePath.IsSet) s.SetProperty(u => u.ProfilePicturePath, c.ProfilePicturePath.Value);
        }, cancellationToken) == 1;
    }

    public async Task<Guid?> FindInformationIdAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.UserInformation.Where(i => i.UserId == userId && i.RowStatus != "DELETED").Select(i => (Guid?)i.Id).ToListAsync(cancellationToken)).SingleOrDefault();

    public async Task<Guid> InsertInformationAsync(Guid userId, ProfileInformationChanges c, CancellationToken cancellationToken)
    {
        var row = new UserInformationRecord
        {
            CreatedBy = userId.ToString(),
            UserId = userId,
            PhoneNumber = Protect(c.PhoneNumber.Value),
            Address = Protect(c.Address.Value),
            DateOfBirth = c.DateOfBirth.Value,
            Gender = c.Gender.Value
        };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> UpdateInformationAsync(Guid informationId, Guid userId, ProfileInformationChanges c, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var by = userId.ToString();
        var phone = Protect(c.PhoneNumber.Value);
        var address = Protect(c.Address.Value);
        return await db.UserInformation.Where(i => i.Id == informationId && i.UserId == userId && i.RowStatus != "DELETED").ExecuteUpdateAsync(s =>
        {
            s.SetProperty(i => i.UpdatedBy, by).SetProperty(i => i.UpdatedAt, nowUtc);
            if (c.PhoneNumber.IsSet) s.SetProperty(i => i.PhoneNumber, phone);
            if (c.Address.IsSet) s.SetProperty(i => i.Address, address);
            if (c.DateOfBirth.IsSet) s.SetProperty(i => i.DateOfBirth, c.DateOfBirth.Value);
            if (c.Gender.IsSet) s.SetProperty(i => i.Gender, c.Gender.Value);
        }, cancellationToken) == 1;
    }

    private byte[]? Protect(string? value) => value is null ? null : protector.Protect(value);
}
