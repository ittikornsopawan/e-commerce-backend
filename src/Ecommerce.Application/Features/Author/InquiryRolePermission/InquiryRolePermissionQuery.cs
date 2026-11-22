using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;

namespace Ecommerce.Application.Features.Author.InquiryRolePermission;

/// <summary>GET v1/author/role/permission (spec 14); query values stay raw strings (null when omitted); the caller is null when the JWT did not verify.</summary>
public sealed record InquiryRolePermissionQuery(
    Caller? Caller, string? Id, string? RoleId, string? PermissionId, string? PageIndex, string? PageSize, string? RowStatus) : IQuery<Page<RolePermissionItem>>;

/// <summary>data.items[] of spec 14, in the spec's field order; Scopes is empty when the grant has none.</summary>
public sealed record RolePermissionItem(
    Guid RolePermissionId, Guid RoleId, string RoleName, Guid PermissionId, string PermissionName, IReadOnlyList<RolePermissionScopeItem> Scopes, string RowStatus);

/// <summary>data.items[].scopes[]: the link, its definition and scope; RowStatus is the link's.</summary>
public sealed record RolePermissionScopeItem(Guid RolePermissionScopeId, Guid PermissionScopeId, Guid ScopeId, string Scope, string RowStatus);
