using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;

namespace Ecommerce.Application.Features.Author.InquiryUserRole;

/// <summary>GET v1/author/user/role (spec 11); query values stay raw strings (null when omitted); the caller is null when the JWT did not verify.</summary>
public sealed record InquiryUserRoleQuery(
    Caller? Caller, string? Id, string? UserId, string? RoleId, string? PageIndex, string? PageSize, string? RowStatus) : IQuery<Page<UserRoleItem>>;

/// <summary>data.items[] of spec 11, in the spec's field order; UserDisplayName is null when the user has none.</summary>
public sealed record UserRoleItem(Guid UserRoleId, Guid UserId, string? UserDisplayName, Guid RoleId, string RoleName, string RowStatus);
