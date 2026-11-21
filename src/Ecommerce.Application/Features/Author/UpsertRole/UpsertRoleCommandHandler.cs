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

namespace Ecommerce.Application.Features.Author.UpsertRole;

/// <summary>
/// Orchestrates spec 01 in one READ COMMITTED transaction: caller lock and current credential (401) → for an id, the
/// locked target (404) → PLATFORM_ADMIN from the token (403) → <see cref="RoleWriter"/> (409s and the guarded write).
/// Every rejection after BEGIN rolls back.
/// </summary>
public sealed class UpsertRoleCommandHandler(
    CallerCredential credential,
    IRoleRepository roles,
    RoleWriter writer,
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
        var written = r.Id.IsSet
            ? await UpdateAsync(caller, Guid.Parse(r.Id.Value!), values, by, verified.Value, ct)
            : await InsertAsync(caller, values, by, verified.Value, ct);
        return written.IsSuccess
            ? Result.Success(new UpsertRoleResult(written.Value.RoleId, written.Value.Action))
            : Result.Failure<UpsertRoleResult>(written.FirstError);
    }

    private Task<Result<RoleWritten>> InsertAsync(Caller caller, RoleValues values, string by, DateTime now, CancellationToken ct) =>
        caller.IsPlatformAdmin
            ? writer.InsertAsync(values, by, now, ct)
            : Task.FromResult(Result.Failure<RoleWritten>(AuthorErrors.ManageRolesForbidden));

    private async Task<Result<RoleWritten>> UpdateAsync(Caller caller, Guid id, RoleValues values, string by, DateTime now, CancellationToken ct)
    {
        var storedName = await roles.LockNameAsync(id, ct);
        if (storedName is null) return Result.Failure<RoleWritten>(AuthorErrors.RoleNotFound);
        if (!caller.IsPlatformAdmin) return Result.Failure<RoleWritten>(AuthorErrors.ManageRolesForbidden);

        return await writer.UpdateAsync(id, storedName, values, by, now, ct);
    }
}
