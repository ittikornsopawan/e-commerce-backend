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
using static Ecommerce.Application.Features.Author.UpsertPermissionBatch.UpsertPermissionBatchCommandValidator;

namespace Ecommerce.Application.Features.Author.UpsertPermissionBatch;

/// <summary>
/// Orchestrates spec 05 in one READ COMMITTED transaction: caller lock and credential (401) → lock every target permission
/// in id order → the first missing target in request order (404) → PLATFORM_ADMIN from the token, once (403) → lock the
/// ACTIVE masters and owned links once → each item in request order through <see cref="PermissionWriter"/>, seeing the
/// writes before it. Any failure rolls back every item; one COMMIT after the loop.
/// </summary>
public sealed class UpsertPermissionBatchCommandHandler(
    CallerCredential credential,
    IPermissionRepository permissions,
    PermissionWriter writer,
    ITransactionRunner transactions) : ICommandHandler<UpsertPermissionBatchCommand, UpsertPermissionBatchResult>
{
    public async Task<Result<UpsertPermissionBatchResult>> HandleAsync(UpsertPermissionBatchCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        var items = r.Permissions.Select(p => PermissionItem.Parse(p.Id, p.Name, p.Description, p.RowStatus, p.Scopes)).ToList();
        var current = new CurrentItem();
        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, items, current, ct), cancellationToken);
        }
        // The unique indexes are the final guards against a concurrent writer; the item being written takes the blame.
        catch (DuplicateKeyException duplicate) when (PermissionWriter.UniqueGuards.TryGetValue(duplicate.ConstraintName, out var taken))
        {
            return taken.At(Position(current.Index));
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertPermissionBatchResult>> RunAsync(
        Caller caller, IReadOnlyList<PermissionItem> items, CurrentItem current, CancellationToken ct)
    {
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Fail(verified.FirstError);

        var locked = await permissions.LockManyAsync(items.Where(i => i.Id is not null).Select(i => i.Id!.Value).ToList(), ct);
        var missing = items.ToList().FindIndex(i => i.Id is { } id && !locked.Contains(id));
        if (missing >= 0) return Fail(AuthorErrors.PermissionNotFound.At(Position(missing)));

        // The permission is the caller's, not an item's: one 403 with no position, before any scope or conflict check.
        if (!caller.IsPlatformAdmin) return Fail(AuthorErrors.ManagePermissionsForbidden);

        var locks = await writer.LockLinksAsync(items, ct);
        return await WriteAllAsync(items, locks, caller.UserId.ToString(), verified.Value, current, ct);
    }

    private async Task<Result<UpsertPermissionBatchResult>> WriteAllAsync(
        IReadOnlyList<PermissionItem> items, LinkLocks locks, string by, DateTime now, CurrentItem current, CancellationToken ct)
    {
        var results = new List<PermissionBatchItemResult>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            current.Index = i;
            if (PermissionWriter.CheckLinks(items[i], locks) is { } linkError) return Fail(linkError.At(Position(i)));

            var written = await writer.WriteAsync(items[i], by, now, ct);
            if (written.IsFailure) return Fail(written.FirstError.At(Position(i)));
            results.Add(new PermissionBatchItemResult(i, written.Value));
        }

        return Result.Success(new UpsertPermissionBatchResult(results));
    }

    private static Result<UpsertPermissionBatchResult> Fail(Error error) => Result.Failure<UpsertPermissionBatchResult>(error);
}
