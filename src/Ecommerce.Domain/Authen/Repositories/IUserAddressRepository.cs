using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The address reads and writes of spec 09-upsert-user-address, all under the user lock.</summary>
public interface IUserAddressRepository
{
    /// <summary>QUERY - Check existing address: owned by the user and not DELETED, or null.</summary>
    Task<OwnedAddress?> FindOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken);

    /// <summary>QUERY - Count addresses: the user's non-DELETED rows.</summary>
    Task<int> CountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>QUERY - Check an ACTIVE address exists, other than <paramref name="excludeId"/>.</summary>
    Task<bool> OtherActiveExistsAsync(Guid userId, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>UPDATE - Clear the old default: every non-DELETED default of the user except <paramref name="excludeId"/>.</summary>
    Task ClearDefaultAsync(Guid userId, Guid? excludeId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>INSERT - Create user address; returns its id.</summary>
    Task<Guid> InsertAsync(Guid userId, AddressValues values, string rowStatus, bool isDefault, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Update or delete user address (guarded by owner and not DELETED); false when no row matched.</summary>
    Task<bool> UpdateAsync(Guid id, Guid userId, AddressValues values, string rowStatus, bool isDefault, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>UPDATE - Promote another default address: the latest changed ACTIVE address, then the lowest id.</summary>
    Task PromoteDefaultAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken);
}
