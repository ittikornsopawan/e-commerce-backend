using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Application.Features.Author.UpsertScopeBatch.UpsertScopeBatchCommandValidator;

namespace Ecommerce.Application.Features.Author.UpsertScopeBatch;

/// <summary>
/// Orchestrates spec 08 in one READ COMMITTED transaction: caller lock and credential (401) → lock every target scope in id
/// order → the first missing target in request order (404) → PLATFORM_ADMIN from the token, once (403) → each item in
/// request order through <see cref="ScopeWriter"/>, seeing the writes before it. Any failure rolls back every item.
/// </summary>
public sealed class UpsertScopeBatchCommandHandler(
    CallerCredential credential,
    IScopeRepository scopes,
    ScopeWriter writer,
    ITransactionRunner transactions) : ICommandHandler<UpsertScopeBatchCommand, UpsertScopeBatchResult>
{
    public async Task<Result<UpsertScopeBatchResult>> HandleAsync(UpsertScopeBatchCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        var current = new CurrentItem();
        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, r.Scopes, current, ct), cancellationToken);
        }
        // uq_m_scope_scope is the final guard against a concurrent insert or rename; the item being written takes the blame.
        catch (DuplicateKeyException duplicate) when (duplicate.ConstraintName == ScopeWriter.UniqueGuard)
        {
            return AuthorErrors.ScopeCodeTaken.At(Position(current.Index));
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertScopeBatchResult>> RunAsync(Caller caller, IReadOnlyList<ScopeBatchItem> items, CurrentItem current, CancellationToken ct)
    {
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Fail(verified.FirstError);

        var ids = items.Select(i => i.Id.IsSet ? Guid.Parse(i.Id.Value!) : (Guid?)null).ToList(); // validated
        var locked = await scopes.LockManyAsync(ids.OfType<Guid>().ToList(), ct);
        var missing = ids.FindIndex(id => id is { } target && !locked.Contains(target));
        if (missing >= 0) return Fail(AuthorErrors.ScopeTargetNotFound.At(Position(missing)));

        // The permission is the caller's, not an item's: one 403 with no position, before any conflict or write.
        if (!caller.IsPlatformAdmin) return Fail(AuthorErrors.ManageScopesForbidden);

        return await WriteAllAsync(items, ids, caller.UserId.ToString(), verified.Value, current, ct);
    }

    private async Task<Result<UpsertScopeBatchResult>> WriteAllAsync(
        IReadOnlyList<ScopeBatchItem> items, IReadOnlyList<Guid?> ids, string by, DateTime now, CurrentItem current, CancellationToken ct)
    {
        var results = new List<ScopeBatchItemResult>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            current.Index = i;
            var values = ScopeWriter.Values(items[i].Scope, items[i].Name, items[i].Description, items[i].RowStatus);
            var written = await writer.WriteAsync(ids[i], values, by, now, ct);
            if (written.IsFailure) return Fail(written.FirstError.At(Position(i)));
            results.Add(new ScopeBatchItemResult(i, written.Value));
        }

        return Result.Success(new UpsertScopeBatchResult(results));
    }

    private static Result<UpsertScopeBatchResult> Fail(Error error) => Result.Failure<UpsertScopeBatchResult>(error);
}
