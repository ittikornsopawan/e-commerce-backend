using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.UpsertPermission;

/// <summary>
/// POST v1/author/permission/upsert (spec 04). Id and RowStatus are unset when omitted; Scopes is empty when omitted or [];
/// the caller is null when the JWT did not verify.
/// </summary>
public sealed record UpsertPermissionCommand(
    Caller? Caller, Optional<string?> Id, string? Name, string? Description, Optional<string?> RowStatus, IReadOnlyList<ScopeLinkInput> Scopes)
    : ICommand<UpsertPermissionResult>;

/// <summary>The written permission and what happened; the API answers 201, 200 or 204 from <c>Action</c>.</summary>
public sealed record UpsertPermissionResult(Guid PermissionId, UpsertAction Action);
