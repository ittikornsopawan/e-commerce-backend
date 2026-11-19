using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;

namespace Ecommerce.Application.Features.Authen.InquiryUserAddress;

/// <summary>
/// GET v1/user/address (spec 10). The query values stay raw strings (null when omitted) so the validator can refuse an
/// explicit empty or invalid value instead of defaulting it. The caller is null when the JWT did not verify.
/// </summary>
public sealed record InquiryUserAddressQuery(
    Caller? Caller, string? Id, string? PageIndex, string? PageSize, string? Search, string? RowStatus) : IQuery<Page<UserAddressItem>>;

/// <summary>data.items[] of spec 10, in the spec's field order.</summary>
public sealed record UserAddressItem(
    Guid UserAddressId, string Title, bool IsDefault, string Address, string City, string State, string PostalCode, string Country, string RowStatus);
