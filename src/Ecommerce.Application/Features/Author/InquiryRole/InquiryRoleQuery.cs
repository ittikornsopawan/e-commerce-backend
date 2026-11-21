using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;

namespace Ecommerce.Application.Features.Author.InquiryRole;

/// <summary>
/// GET v1/author/role (spec 03). The query values stay raw strings (null when omitted) so the validator can refuse an
/// explicit empty or invalid value instead of defaulting it. The caller is null when the JWT did not verify.
/// </summary>
public sealed record InquiryRoleQuery(
    Caller? Caller, string? Id, string? PageIndex, string? PageSize, string? Search, string? RowStatus) : IQuery<Page<RoleItem>>;

/// <summary>data.items[] of spec 03, in the spec's field order.</summary>
public sealed record RoleItem(Guid RoleId, string Name, string Description, string RowStatus);
