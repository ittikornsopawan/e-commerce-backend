using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.UpsertScopeBatch;

/// <summary>One scope of the batch; Id and RowStatus are unset when omitted.</summary>
public sealed record ScopeBatchItem(Optional<string?> Id, string? Scope, string? Name, string? Description, Optional<string?> RowStatus);

/// <summary>POST v1/author/scope/upsert/batch (spec 08); the caller is null when the JWT did not verify.</summary>
public sealed record UpsertScopeBatchCommand(Caller? Caller, IReadOnlyList<ScopeBatchItem> Scopes) : ICommand<UpsertScopeBatchResult>;

/// <summary>One committed item: its zero-based request position, the scope id from the write and what happened.</summary>
public sealed record ScopeBatchItemResult(int Index, ScopeWritten Written);

/// <summary>Every item's result in request order.</summary>
public sealed record UpsertScopeBatchResult(IReadOnlyList<ScopeBatchItemResult> Scopes);
