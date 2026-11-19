using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.UpsertUserAddress;

/// <summary>
/// POST v1/user/address/upsert (spec 09). An unset <c>Id</c> inserts; a set one updates or, with DELETED, soft deletes.
/// <c>Id</c> and <c>RowStatus</c> keep presence because a JSON null is invalid while an absent key is not.
/// </summary>
public sealed record UpsertUserAddressCommand(
    Caller? Caller,
    Optional<string?> Id,
    string? Title,
    bool? IsDefault,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    Optional<string?> RowStatus) : ICommand<UpsertUserAddressResult>;

public enum AddressAction
{
    Insert,
    Update,
    Delete
}

/// <summary>The written address and what happened; the API answers 201, 200 or 204 from <c>Action</c>.</summary>
public sealed record UpsertUserAddressResult(Guid UserAddressId, AddressAction Action);
