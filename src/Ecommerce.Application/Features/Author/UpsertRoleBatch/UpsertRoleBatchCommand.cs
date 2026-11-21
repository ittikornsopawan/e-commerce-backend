using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.UpsertRoleBatch;

/// <summary>One role of the batch; Id and RowStatus are unset when omitted.</summary>
public sealed record RoleBatchItem(Optional<string?> Id, string? Name, string? Description, Optional<string?> RowStatus);

/// <summary>POST v1/author/role/upsert/batch (spec 02); the caller is null when the JWT did not verify.</summary>
public sealed record UpsertRoleBatchCommand(Caller? Caller, IReadOnlyList<RoleBatchItem> Roles) : ICommand<UpsertRoleBatchResult>;

/// <summary>One committed item: its zero-based request position, the role id from the write and what happened.</summary>
public sealed record RoleBatchItemResult(int Index, RoleWritten Written);

/// <summary>Every item's result in request order.</summary>
public sealed record UpsertRoleBatchResult(IReadOnlyList<RoleBatchItemResult> Roles);
