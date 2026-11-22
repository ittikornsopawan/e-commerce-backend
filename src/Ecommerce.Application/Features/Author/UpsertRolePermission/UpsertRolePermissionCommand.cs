using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.UpsertRolePermission;

/// <summary>
/// POST v1/author/role/permission/upsert (spec 13). Id and RowStatus are unset when omitted; Scopes is empty when omitted or
/// []; the caller is null when the JWT did not verify.
/// </summary>
public sealed record UpsertRolePermissionCommand(
    Caller? Caller, Optional<string?> Id, string? RoleId, string? PermissionId, Optional<string?> RowStatus, IReadOnlyList<ScopeLinkInput> Scopes)
    : ICommand<UpsertRolePermissionResult>;

/// <summary>The written grant and what happened; the API answers 201, 200 or 204 from <c>Action</c>.</summary>
public sealed record UpsertRolePermissionResult(Guid RolePermissionId, UpsertAction Action);
