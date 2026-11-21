using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.UpsertPermissionBatch;

/// <summary>One permission of the batch; Id and RowStatus are unset when omitted, Scopes empty when omitted or [].</summary>
public sealed record PermissionBatchItem(
    Optional<string?> Id, string? Name, string? Description, Optional<string?> RowStatus, IReadOnlyList<ScopeLinkInput> Scopes);

/// <summary>POST v1/author/permission/upsert/batch (spec 05); the caller is null when the JWT did not verify.</summary>
public sealed record UpsertPermissionBatchCommand(Caller? Caller, IReadOnlyList<PermissionBatchItem> Permissions) : ICommand<UpsertPermissionBatchResult>;

/// <summary>One committed item: its zero-based request position, the permission id from the write and what happened.</summary>
public sealed record PermissionBatchItemResult(int Index, PermissionWritten Written);

/// <summary>Every item's result in request order.</summary>
public sealed record UpsertPermissionBatchResult(IReadOnlyList<PermissionBatchItemResult> Permissions);
