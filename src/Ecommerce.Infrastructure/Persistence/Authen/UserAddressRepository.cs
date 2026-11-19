using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Persistence.Ef;
using Ecommerce.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Persistence.Authen;

/// <summary>EF Core LINQ for the address SQL of spec 09; address, city, state, postal code and country are encrypted.</summary>
internal sealed class UserAddressRepository(EcommerceDbContext db, FieldProtector protector) : IUserAddressRepository
{
    private const string Deleted = "DELETED";

    public Task<OwnedAddress?> FindOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.UserAddresses
            .Where(a => a.Id == id && a.UserId == userId && a.RowStatus != Deleted)
            .Select(a => new OwnedAddress(a.Id, a.IsDefault))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountAsync(Guid userId, CancellationToken cancellationToken) =>
        db.UserAddresses.CountAsync(a => a.UserId == userId && a.RowStatus != Deleted, cancellationToken);

    public Task<bool> OtherActiveExistsAsync(Guid userId, Guid? excludeId, CancellationToken cancellationToken) =>
        db.UserAddresses.AnyAsync(a => a.UserId == userId && a.RowStatus == "ACTIVE" && (excludeId == null || a.Id != excludeId), cancellationToken);

    public Task ClearDefaultAsync(Guid userId, Guid? excludeId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var by = userId.ToString();
        return db.UserAddresses
            .Where(a => a.UserId == userId && a.IsDefault && a.RowStatus != Deleted && (excludeId == null || a.Id != excludeId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.UpdatedBy, by)
                .SetProperty(a => a.UpdatedAt, nowUtc)
                .SetProperty(a => a.IsDefault, false), cancellationToken);
    }

    public async Task<Guid> InsertAsync(
        Guid userId, AddressValues values, string rowStatus, bool isDefault, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var row = new UserAddressRecord
        {
            CreatedBy = userId.ToString(),
            CreatedAt = nowUtc,
            RowStatus = rowStatus,
            UserId = userId,
            Title = values.Title,
            IsDefault = isDefault,
            Address = protector.Protect(values.Address),
            City = protector.Protect(values.City),
            State = protector.Protect(values.State),
            PostalCode = protector.Protect(values.PostalCode),
            Country = protector.Protect(values.Country)
        };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(
        Guid id, Guid userId, AddressValues v, string rowStatus, bool isDefault, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var (by, address, city, state, postalCode, country) = (userId.ToString(), protector.Protect(v.Address), protector.Protect(v.City),
            protector.Protect(v.State), protector.Protect(v.PostalCode), protector.Protect(v.Country));

        var updated = await db.UserAddresses
            .Where(a => a.Id == id && a.UserId == userId && a.RowStatus != Deleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.UpdatedBy, by).SetProperty(a => a.UpdatedAt, nowUtc).SetProperty(a => a.RowStatus, rowStatus)
                .SetProperty(a => a.Title, v.Title).SetProperty(a => a.IsDefault, isDefault).SetProperty(a => a.Address, address)
                .SetProperty(a => a.City, city).SetProperty(a => a.State, state).SetProperty(a => a.PostalCode, postalCode)
                .SetProperty(a => a.Country, country), cancellationToken);
        return updated == 1;
    }

    public async Task PromoteDefaultAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // The most recently changed ACTIVE address takes over; the id breaks ties so the choice is deterministic.
        // The spec's single UPDATE with a sub-select becomes a select then an update; the user lock keeps them atomic.
        var nextId = await db.UserAddresses
            .Where(a => a.UserId == userId && a.RowStatus == "ACTIVE")
            .OrderByDescending(a => a.UpdatedAt ?? a.CreatedAt)
            .ThenBy(a => a.Id)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (nextId is null) return;

        var by = userId.ToString();
        await db.UserAddresses
            .Where(a => a.Id == nextId && a.UserId == userId && a.RowStatus == "ACTIVE")
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UpdatedBy, by).SetProperty(a => a.UpdatedAt, nowUtc).SetProperty(a => a.IsDefault, true), cancellationToken);
    }
}
