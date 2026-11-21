using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.UpsertScope;

/// <summary>POST v1/author/scope/upsert (spec 07). Id and RowStatus are unset when omitted; the caller is null when the JWT did not verify.</summary>
public sealed record UpsertScopeCommand(
    Caller? Caller, Optional<string?> Id, string? Scope, string? Name, string? Description, Optional<string?> RowStatus) : ICommand<UpsertScopeResult>;

/// <summary>The written scope and what happened; the API answers 201, 200 or 204 from <c>Action</c>.</summary>
public sealed record UpsertScopeResult(Guid ScopeId, UpsertAction Action);
