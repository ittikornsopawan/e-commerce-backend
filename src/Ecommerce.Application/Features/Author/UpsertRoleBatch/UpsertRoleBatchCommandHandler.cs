using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Application.Features.Author.UpsertRoleBatch.UpsertRoleBatchCommandValidator;

namespace Ecommerce.Application.Features.Author.UpsertRoleBatch;

/// <summary>
/// Orchestrates spec 02 in one READ COMMITTED transaction: caller lock and current credential (401) → lock every target
/// id in ascending order → the first missing target in request order (404) → PLATFORM_ADMIN from the token, once (403)
/// → each item in request order through <see cref="RoleWriter"/>, seeing the writes before it. Any failure rolls back
/// every item; one COMMIT after the loop.
/// </summary>
public sealed class UpsertRoleBatchCommandHandler(
    CallerCredential credential,
    IRoleRepository roles,
    RoleWriter writer,
    ITransactionRunner transactions) : ICommandHandler<UpsertRoleBatchCommand, UpsertRoleBatchResult>
{
    public async Task<Result<UpsertRoleBatchResult>> HandleAsync(UpsertRoleBatchCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        var current = new CurrentItem();
        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, r.Roles, current, ct), cancellationToken);
        }
        // uq_m_role_name is the final guard against a concurrent insert or rename; the item being written takes the blame.
        catch (DuplicateKeyException duplicate) when (duplicate.ConstraintName == "uq_m_role_name")
        {
            return AuthorErrors.RoleNameTaken.At(Position(current.Index));
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertRoleBatchResult>> RunAsync(
        Caller caller, IReadOnlyList<RoleBatchItem> items, CurrentItem current, CancellationToken ct)
    {
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Result.Failure<UpsertRoleBatchResult>(verified.FirstError);

        var stored = await roles.LockNamesAsync(items.Where(i => i.Id.IsSet).Select(IdOf).ToList(), ct);
        var missing = FirstMissing(items, stored);
        if (missing >= 0) return Result.Failure<UpsertRoleBatchResult>(AuthorErrors.RoleNotFound.At(Position(missing)));

        // The permission is the caller's, not an item's: one 403 with no position, before any conflict or write.
        if (!caller.IsPlatformAdmin) return Result.Failure<UpsertRoleBatchResult>(AuthorErrors.ManageRolesForbidden);

        return await WriteAllAsync(items, stored, caller.UserId.ToString(), verified.Value, current, ct);
    }

    private async Task<Result<UpsertRoleBatchResult>> WriteAllAsync(
        IReadOnlyList<RoleBatchItem> items, IReadOnlyDictionary<Guid, string> stored, string by, DateTime now, CurrentItem current, CancellationToken ct)
    {
        var results = new List<RoleBatchItemResult>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            current.Index = i;
            var written = await WriteAsync(items[i], stored, by, now, ct);
            if (written.IsFailure) return Result.Failure<UpsertRoleBatchResult>(written.FirstError.At(Position(i)));
            results.Add(new RoleBatchItemResult(i, written.Value));
        }

        return Result.Success(new UpsertRoleBatchResult(results));
    }

    private Task<Result<RoleWritten>> WriteAsync(
        RoleBatchItem item, IReadOnlyDictionary<Guid, string> stored, string by, DateTime now, CancellationToken ct)
    {
        // The validator has checked every value.
        var values = new RoleValues(item.Name!, item.Description!, item.RowStatus.IsSet ? item.RowStatus.Value! : MasterLifecycle.Active);
        if (!item.Id.IsSet) return writer.InsertAsync(values, by, now, ct);

        var id = IdOf(item);
        return writer.UpdateAsync(id, stored[id], values, by, now, ct);
    }

    private static int FirstMissing(IReadOnlyList<RoleBatchItem> items, IReadOnlyDictionary<Guid, string> stored)
    {
        for (var i = 0; i < items.Count; i++)
            if (items[i].Id.IsSet && !stored.ContainsKey(IdOf(items[i]))) return i;
        return -1;
    }

    private static Guid IdOf(RoleBatchItem item) => Guid.Parse(item.Id.Value!);
}
