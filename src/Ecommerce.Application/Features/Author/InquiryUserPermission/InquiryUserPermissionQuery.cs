using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;

namespace Ecommerce.Application.Features.Author.InquiryUserPermission;

/// <summary>GET v1/author/user/permission (spec 16); query values stay raw strings (null when omitted); the caller is null when the JWT did not verify.</summary>
public sealed record InquiryUserPermissionQuery(
    Caller? Caller, string? Id, string? UserId, string? PermissionId, string? PageIndex, string? PageSize, string? RowStatus) : IQuery<Page<UserPermissionItem>>;

/// <summary>data.items[] of spec 16, in the spec's field order; the three scope fields are null when the grant has no scope.</summary>
public sealed record UserPermissionItem(
    Guid UserPermissionId, Guid UserId, string? UserDisplayName, Guid PermissionId, string PermissionName,
    Guid? PermissionScopeId, Guid? ScopeId, string? Scope, string RowStatus);
