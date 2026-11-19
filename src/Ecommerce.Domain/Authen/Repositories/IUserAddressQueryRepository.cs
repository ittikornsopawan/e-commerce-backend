namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The identical predicates of the count and the page; a null filter is disabled (typed SQL NULL).</summary>
public sealed record AddressFilter(Guid UserId, Guid? Id, string? SearchPattern, string? RowStatus);

/// <summary>One non-DELETED address of the caller, the five sensitive fields decrypted.</summary>
public sealed record AddressRow(
    Guid Id, string Title, bool IsDefault, string Address, string City, string State, string PostalCode, string Country, string RowStatus);

/// <summary>The address reads of spec 10-inquiry-user-address, in the caller's read snapshot; no lock, no write.</summary>
public interface IUserAddressQueryRepository
{
    /// <summary>QUERY - Get total items.</summary>
    Task<long> CountAsync(AddressFilter filter, CancellationToken cancellationToken);

    /// <summary>QUERY - Get user addresses: default first, then created_at DESC, then id.</summary>
    Task<IReadOnlyList<AddressRow>> ListAsync(AddressFilter filter, long offset, long limit, CancellationToken cancellationToken);
}
