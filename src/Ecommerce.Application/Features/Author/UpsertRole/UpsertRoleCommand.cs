using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.UpsertRole;

/// <summary>POST v1/author/role/upsert (spec 01). Id and RowStatus are unset when omitted; the caller is null when the JWT did not verify.</summary>
public sealed record UpsertRoleCommand(
    Caller? Caller, Optional<string?> Id, string? Name, string? Description, Optional<string?> RowStatus) : ICommand<UpsertRoleResult>;

/// <summary>The written role and what happened; the API answers 201, 200 or 204 from <c>Action</c>.</summary>
public sealed record UpsertRoleResult(Guid RoleId, UpsertAction Action);
