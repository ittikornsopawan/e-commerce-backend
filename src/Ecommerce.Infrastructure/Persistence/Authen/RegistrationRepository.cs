using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for the SQL of spec 01-register (Database Relations). Personal fields are encrypted here.</summary>
internal sealed class RegistrationRepository(EcommerceDbContext db, FieldProtector protector) : IRegistrationRepository
{
    /// <summary>The server service identifier the spec stores in created_by.</summary>
    public const string CreatedBy = "auth-register";

    public Task<bool> IdentifierExistsAsync(string username, string emailAddress, CancellationToken cancellationToken)
    {
        var usernameBytes = protector.Protect(username);
        var emailBytes = protector.Protect(emailAddress);
        return db.Users.AnyAsync(u => u.Username == usernameBytes || u.EmailAddress == emailBytes, cancellationToken);
    }

    public async Task<IReadOnlyList<ApplicableConsent>> GetApplicableConsentsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        // QUERY - Get effective consents
        var effective = await db.Consents
            .Where(c => c.RowStatus == "ACTIVE" && c.EffectiveAt <= nowUtc && (c.ExpiredAt == null || c.ExpiredAt > nowUtc))
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.ConsentTypeId, c.ConsentVersion, c.IsRequired })
            .ToListAsync(cancellationToken);

        // QUERY - Get active consent types
        var typeIds = effective.Select(c => c.ConsentTypeId).Distinct().ToList();
        var activeTypeIds = await db.ConsentTypes
            .Where(t => typeIds.Contains(t.Id) && t.RowStatus == "ACTIVE")
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        return effective
            .Where(c => activeTypeIds.Contains(c.ConsentTypeId))
            .Select(c => new ApplicableConsent(c.Id, c.ConsentVersion, c.IsRequired))
            .ToList();
    }

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId, cancellationToken);

    public async Task<Guid> AddAsync(NewRegistration r, CancellationToken cancellationToken)
    {
        // Saved step by step to keep the insert order of the spec; the user id comes back from the database.
        var userId = await AddUserAsync(r, cancellationToken);
        await AddAddressAsync(userId, r.DefaultAddress, cancellationToken);
        await AddConsentsAsync(userId, r, cancellationToken);

        if (r.ReferrerId is { } referrerId)
            await SaveAsync(new UserReferralRecord { CreatedBy = CreatedBy, UserId = userId, ReferrerId = referrerId }, cancellationToken);

        return userId;
    }

    /// <summary>INSERT m_user, then its password and information rows.</summary>
    private async Task<Guid> AddUserAsync(NewRegistration r, CancellationToken cancellationToken)
    {
        var user = new UserRecord
        {
            CreatedBy = CreatedBy,
            Username = protector.Protect(r.Username),
            EmailAddress = protector.Protect(r.EmailAddress),
            FirstName = ProtectOrNull(r.FirstName),
            LastName = ProtectOrNull(r.LastName),
            DisplayName = r.DisplayName,
            ProfilePicturePath = r.ProfilePicturePath
        };
        await SaveAsync(user, cancellationToken);

        await SaveAsync(new UserPasswordRecord
        {
            CreatedBy = CreatedBy, RowType = "PASSWORD", UserId = user.Id, Password = r.Password.Hash, PasswordSalt = r.Password.Salt
        }, cancellationToken);

        await SaveAsync(new UserInformationRecord
        {
            CreatedBy = CreatedBy, UserId = user.Id, PhoneNumber = ProtectOrNull(r.PhoneNumber), Address = ProtectOrNull(r.Address),
            DateOfBirth = r.DateOfBirth, Gender = r.Gender
        }, cancellationToken);
        return user.Id;
    }

    private async Task AddAddressAsync(Guid userId, RegistrationAddress? address, CancellationToken cancellationToken)
    {
        if (address is not { } a) return;

        await SaveAsync(new UserAddressRecord
        {
            CreatedBy = CreatedBy, UserId = userId, Title = a.Title, IsDefault = true, // the first address is always default
            Address = protector.Protect(a.Address), City = protector.Protect(a.City), State = protector.Protect(a.State),
            PostalCode = protector.Protect(a.PostalCode), Country = protector.Protect(a.Country)
        }, cancellationToken);
    }

    private async Task AddConsentsAsync(Guid userId, NewRegistration r, CancellationToken cancellationToken)
    {
        // consentId order as PostgreSQL sorts uuid (its lower-case text).
        foreach (var consent in r.Consents.OrderBy(c => c.ConsentId.ToString(), StringComparer.Ordinal))
        {
            await SaveAsync(new UserConsentRecord
            {
                CreatedBy = CreatedBy, UserId = userId, ConsentId = consent.ConsentId, ConsentVersion = consent.Version,
                Result = consent.Result ?? false, IsRead = true, ReadAt = r.NowUtc
            }, cancellationToken);
        }
    }

    private async Task SaveAsync<TRecord>(TRecord record, CancellationToken cancellationToken) where TRecord : class
    {
        db.Add(record);
        await db.SaveChangesAsync(cancellationToken);
    }

    private byte[]? ProtectOrNull(string? value) => value is null ? null : protector.Protect(value);
}
