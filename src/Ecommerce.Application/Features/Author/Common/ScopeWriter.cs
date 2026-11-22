using Ecommerce.Application.Common;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>A written scope and what happened to it.</summary>
public sealed record ScopeWritten(Guid ScopeId, UpsertAction Action);

/// <summary>
/// The scope write of specs 07 and 08, after the target lock and the PLATFORM_ADMIN check, inside the caller's transaction:
/// duplicate code (409), in use by a permission before DELETED/ARCHIVED (409), then the insert or the guarded update. A
/// batch item sees the writes of the items before it.
/// </summary>
public sealed class ScopeWriter(IScopeRepository scopes)
{
    /// <summary>The unique index that is the final guard against a concurrent writer with the same code.</summary>
    public const string UniqueGuard = "uq_m_scope_scope";

    /// <summary>The validator has checked every value.</summary>
    public static ScopeValues Values(string? scope, string? name, string? description, Optional<string?> rowStatus) =>
        new(scope!, name!, description!, rowStatus.IsSet ? rowStatus.Value! : MasterLifecycle.Active);

    public async Task<Result<ScopeWritten>> WriteAsync(Guid? id, ScopeValues values, string by, DateTime now, CancellationToken ct)
    {
        if (await scopes.CodeTakenAsync(values.Scope, id, ct)) return Result.Failure<ScopeWritten>(AuthorErrors.ScopeCodeTaken);
        if (id is { } target && MasterLifecycle.TakesOutOfUse(values.RowStatus) && await scopes.InUseAsync(target, ct))
            return Result.Failure<ScopeWritten>(AuthorErrors.ScopeInUse);

        if (id is not { } existing) return Result.Success(new ScopeWritten(await scopes.InsertAsync(values, by, now, ct), UpsertAction.Insert));
        if (!await scopes.UpdateAsync(existing, values, by, now, ct)) return Result.Failure<ScopeWritten>(AuthorErrors.ScopeTargetNotFound);
        return Result.Success(new ScopeWritten(existing, MasterRules.UpdateAction(values.RowStatus)));
    }
}
