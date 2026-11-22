using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.UpsertUserPermission;

/// <summary>POST v1/author/user/permission/upsert (spec 15). Id, ScopeId and RowStatus are unset when omitted; the caller is null when the JWT did not verify.</summary>
public sealed record UpsertUserPermissionCommand(
    Caller? Caller, Optional<string?> Id, string? UserId, string? PermissionId, Optional<string?> ScopeId, Optional<string?> RowStatus)
    : ICommand<UpsertUserPermissionResult>;

/// <summary>The written grant and what happened; the API answers 201, 200 or 204 from <c>Action</c>.</summary>
public sealed record UpsertUserPermissionResult(Guid UserPermissionId, UpsertAction Action);
