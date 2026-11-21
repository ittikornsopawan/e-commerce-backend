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

namespace Ecommerce.Application.Features.Author.UpsertUserRole;

/// <summary>
/// Orchestrates spec 10 in one READ COMMITTED transaction: lock the caller and the target user (id order) → current
/// credential (401) → target user exists (404 E10005) → lock the role → the locked assignment (404) and its unchanged user
/// and role (400) → when the request gives the role, an ACTIVE user and role (404 E10005) → PLATFORM_ADMIN (403) →
/// duplicate (409) → write → at least one administrator left (409). Every rejection after BEGIN rolls back.
/// </summary>
public sealed class UpsertUserRoleCommandHandler(
    CallerCredential credential,
    IUserRoleRepository userRoles,
    ITransactionRunner transactions) : ICommandHandler<UpsertUserRoleCommand, UpsertUserRoleResult>
{
    /// <summary>The validated request: Id when it updates an existing assignment.</summary>
    private sealed record Request(Guid? Id, Guid UserId, Guid RoleId, string RowStatus);

    public async Task<Result<UpsertUserRoleResult>> HandleAsync(UpsertUserRoleCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        // The validator has checked every value.
        var q = new Request(r.Id.IsSet ? Guid.Parse(r.Id.Value!) : null, Guid.Parse(r.UserId!), Guid.Parse(r.RoleId!),
            r.RowStatus.IsSet ? r.RowStatus.Value! : MasterLifecycle.Active);
        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, q, ct), cancellationToken);
        }
        // uq_t_user_role_user_id_role_id is the final guard against a concurrent assignment of the same role.
        catch (DuplicateKeyException duplicate) when (duplicate.ConstraintName == "uq_t_user_role_user_id_role_id")
        {
            return AuthorErrors.UserRoleTaken;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertUserRoleResult>> RunAsync(Caller caller, Request q, CancellationToken ct)
    {
        // Users first, in id order, as every assignment writer does; the caller's credential is then read under that lock.
        var users = await userRoles.LockUsersAsync(new[] { caller.UserId, q.UserId }.Distinct().ToList(), ct);
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Fail(verified.FirstError);
        if (!users.TryGetValue(q.UserId, out var userStatus)) return Fail(AuthorErrors.TargetUserNotFound);

        var role = await userRoles.LockRoleAsync(q.RoleId, ct);
        var row = q.Id is { } id ? await userRoles.LockAssignmentAsync(id, ct) : null;
        if (CheckRow(q, row) is { } rowError) return Fail(rowError);
        if (GivesRole(q, row) && (userStatus != MasterLifecycle.Active || role?.RowStatus != MasterLifecycle.Active))
            return Fail(AuthorErrors.UserOrRoleNotActive);

        if (!caller.IsPlatformAdmin) return Fail(AuthorErrors.AssignRolesForbidden);
        if (await userRoles.TakenAsync(q.UserId, q.RoleId, q.Id, ct)) return Fail(AuthorErrors.UserRoleTaken);
        return await WriteAsync(q, caller.UserId.ToString(), verified.Value, ct);
    }

    /// <summary>An id names a non-DELETED assignment (404) of the same user and role (400): both are fixed after the insert.</summary>
    private static Error? CheckRow(Request q, UserRoleRow? row) => q.Id switch
    {
        null => null,
        _ when row is null => AuthorErrors.UserRoleNotFound,
        _ when row.UserId != q.UserId || row.RoleId != q.RoleId => AuthorErrors.UserRoleChanged,
        _ => null
    };

    /// <summary>An insert, or an update that makes a row ACTIVE again, gives the role; taking it away needs no ACTIVE user or role.</summary>
    private static bool GivesRole(Request q, UserRoleRow? row) =>
        row is null || (q.RowStatus == MasterLifecycle.Active && row.RowStatus != MasterLifecycle.Active);

    private async Task<Result<UpsertUserRoleResult>> WriteAsync(Request q, string by, DateTime now, CancellationToken ct)
    {
        var id = q.Id ?? await userRoles.InsertAsync(q.UserId, q.RoleId, q.RowStatus, by, now, ct);
        if (q.Id is not null && !await userRoles.UpdateAsync(id, q.RowStatus, by, now, ct)) return Fail(AuthorErrors.UserRoleNotFound);

        // On the state after the write: no request may leave the platform without an ACTIVE administrator.
        if (await userRoles.AdministratorsAsync(ct) == 0) return Fail(AuthorErrors.LastAdministrator);
        return Result.Success(new UpsertUserRoleResult(id, q.Id is null ? UpsertAction.Insert : MasterRules.UpdateAction(q.RowStatus)));
    }

    private static Result<UpsertUserRoleResult> Fail(Error error) => Result.Failure<UpsertUserRoleResult>(error);
}
