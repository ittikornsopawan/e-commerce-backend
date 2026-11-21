using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.UpsertUserRole;

/// <summary>POST v1/author/user/role/upsert (spec 10). Id and RowStatus are unset when omitted; the caller is null when the JWT did not verify.</summary>
public sealed record UpsertUserRoleCommand(
    Caller? Caller, Optional<string?> Id, string? UserId, string? RoleId, Optional<string?> RowStatus) : ICommand<UpsertUserRoleResult>;

/// <summary>The written assignment and what happened; the API answers 201, 200 or 204 from <c>Action</c>.</summary>
public sealed record UpsertUserRoleResult(Guid UserRoleId, UpsertAction Action);
