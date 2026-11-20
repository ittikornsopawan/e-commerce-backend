using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Domain;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.UpsertRole;

/// <summary>
/// Orchestrates spec 01 in one READ COMMITTED transaction: caller lock and current credential (401) → for an id, the
/// locked target (404) → PLATFORM_ADMIN from the token (403) → system role and duplicate name (409) → in use before
/// DELETED/ARCHIVED (409) → guarded write. Every rejection after BEGIN rolls back.
/// </summary>
public sealed class UpsertRoleCommandHandler(
    CallerCredential credential,
    IRoleRepository roles,
    ITransactionRunner transactions) : ICommandHandler<UpsertRoleCommand, UpsertRoleResult>
{
    public async Task<Result<UpsertRoleResult>> HandleAsync(UpsertRoleCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, r, ct), cancellationToken);
        }
        // uq_m_role_name is the final guard against a concurrent insert or rename with the same name.
        catch (DuplicateKeyException duplicate) when (duplicate.ConstraintName == "uq_m_role_name")
        {
            return AuthorErrors.RoleNameTaken;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertRoleResult>> RunAsync(Caller caller, UpsertRoleCommand r, CancellationToken ct)
    {
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Result.Failure<UpsertRoleResult>(verified.FirstError);

        // The validator has checked every value.
        var values = new RoleValues(r.Name!, r.Description!, r.RowStatus.IsSet ? r.RowStatus.Value! : MasterLifecycle.Active);
        var by = caller.UserId.ToString();
        return r.Id.IsSet
            ? await UpdateAsync(caller, Guid.Parse(r.Id.Value!), values, by, verified.Value, ct)
            : await InsertAsync(caller, values, by, verified.Value, ct);
    }

    private async Task<Result<UpsertRoleResult>> InsertAsync(Caller caller, RoleValues values, string by, DateTime now, CancellationToken ct)
    {
        if (!caller.IsPlatformAdmin) return Result.Failure<UpsertRoleResult>(AuthorErrors.ManageRolesForbidden);
        if (await roles.NameTakenAsync(values.Name, null, ct)) return Result.Failure<UpsertRoleResult>(AuthorErrors.RoleNameTaken);

        var id = await roles.InsertAsync(values, by, now, ct);
        return Result.Success(new UpsertRoleResult(id, UpsertAction.Insert));
    }

    private async Task<Result<UpsertRoleResult>> UpdateAsync(Caller caller, Guid id, RoleValues values, string by, DateTime now, CancellationToken ct)
    {
        var storedName = await roles.LockNameAsync(id, ct);
        if (storedName is null) return Result.Failure<UpsertRoleResult>(AuthorErrors.RoleNotFound);
        if (!caller.IsPlatformAdmin) return Result.Failure<UpsertRoleResult>(AuthorErrors.ManageRolesForbidden);

        // The stored name decides, not the proposed one: the seeded system role is never changed here.
        if (storedName == RoleCode.PlatformAdmin) return Result.Failure<UpsertRoleResult>(AuthorErrors.SystemRoleLocked);
        if (await roles.NameTakenAsync(values.Name, id, ct)) return Result.Failure<UpsertRoleResult>(AuthorErrors.RoleNameTaken);
        if (MasterLifecycle.TakesOutOfUse(values.RowStatus) && await roles.InUseAsync(id, ct))
            return Result.Failure<UpsertRoleResult>(AuthorErrors.RoleInUse);

        if (!await roles.UpdateAsync(id, values, by, now, ct)) return Result.Failure<UpsertRoleResult>(AuthorErrors.RoleNotFound);
        return Result.Success(new UpsertRoleResult(id, ActionOf(values.RowStatus)));
    }

    private static UpsertAction ActionOf(string rowStatus) => rowStatus switch
    {
        MasterLifecycle.Deleted => UpsertAction.Delete,
        MasterLifecycle.Archived => UpsertAction.Archive,
        _ => UpsertAction.Update
    };
}
