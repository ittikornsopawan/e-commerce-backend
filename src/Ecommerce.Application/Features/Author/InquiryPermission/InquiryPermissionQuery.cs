using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Paging;

namespace Ecommerce.Application.Features.Author.InquiryPermission;

/// <summary>
/// GET v1/author/permission (spec 06). The query values stay raw strings (null when omitted) so the validator can refuse
/// an explicit empty or invalid value. The caller is null when the JWT did not verify.
/// </summary>
public sealed record InquiryPermissionQuery(
    Caller? Caller, string? Id, string? PageIndex, string? PageSize, string? Search, string? RowStatus) : IQuery<Page<PermissionItem>>;

/// <summary>data.items[] of spec 06, in the spec's field order; Scopes is empty when the permission has none.</summary>
public sealed record PermissionItem(Guid PermissionId, string Name, string Description, IReadOnlyList<PermissionScopeItem> Scopes, string RowStatus);

/// <summary>data.items[].scopes[]: the link with its master's code and name; RowStatus is the link's.</summary>
public sealed record PermissionScopeItem(Guid PermissionScopeId, Guid ScopeId, string Scope, string ScopeName, string RowStatus);
