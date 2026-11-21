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

namespace Ecommerce.Application.Features.Author.UpsertScope;

/// <summary>
/// Orchestrates spec 07 in one READ COMMITTED transaction: caller lock and credential (401) → for an id, the locked scope
/// (404) → PLATFORM_ADMIN from the token (403) → duplicate code (409) → in use before DELETED/ARCHIVED (409) → guarded
/// write. Every rejection after BEGIN rolls back.
/// </summary>
public sealed class UpsertScopeCommandHandler(
    CallerCredential credential,
    IScopeRepository scopes,
    ITransactionRunner transactions) : ICommandHandler<UpsertScopeCommand, UpsertScopeResult>
{
    public async Task<Result<UpsertScopeResult>> HandleAsync(UpsertScopeCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, r, ct), cancellationToken);
        }
        // uq_m_scope_scope is the final guard against a concurrent insert or rename with the same code.
        catch (DuplicateKeyException duplicate) when (duplicate.ConstraintName == "uq_m_scope_scope")
        {
            return AuthorErrors.ScopeCodeTaken;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertScopeResult>> RunAsync(Caller caller, UpsertScopeCommand r, CancellationToken ct)
    {
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Fail(verified.FirstError);

        // The validator has checked every value.
        Guid? id = r.Id.IsSet ? Guid.Parse(r.Id.Value!) : null;
        var values = new ScopeValues(r.Scope!, r.Name!, r.Description!, r.RowStatus.IsSet ? r.RowStatus.Value! : MasterLifecycle.Active);
        if (id is { } existing && !await scopes.LockAsync(existing, ct)) return Fail(AuthorErrors.ScopeTargetNotFound);
        if (!caller.IsPlatformAdmin) return Fail(AuthorErrors.ManageScopesForbidden);

        if (await scopes.CodeTakenAsync(values.Scope, id, ct)) return Fail(AuthorErrors.ScopeCodeTaken);
        if (id is { } target && MasterLifecycle.TakesOutOfUse(values.RowStatus) && await scopes.InUseAsync(target, ct)) return Fail(AuthorErrors.ScopeInUse);

        return await WriteAsync(id, values, caller.UserId.ToString(), verified.Value, ct);
    }

    private async Task<Result<UpsertScopeResult>> WriteAsync(Guid? id, ScopeValues values, string by, DateTime now, CancellationToken ct)
    {
        if (id is not { } existing) return Result.Success(new UpsertScopeResult(await scopes.InsertAsync(values, by, now, ct), UpsertAction.Insert));
        if (!await scopes.UpdateAsync(existing, values, by, now, ct)) return Fail(AuthorErrors.ScopeTargetNotFound);
        return Result.Success(new UpsertScopeResult(existing, MasterRules.UpdateAction(values.RowStatus)));
    }

    private static Result<UpsertScopeResult> Fail(Error error) => Result.Failure<UpsertScopeResult>(error);
}
