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

namespace Ecommerce.Application.Features.Author.UpsertUserPermission;

/// <summary>
/// Orchestrates spec 15 in one READ COMMITTED transaction: lock the caller and the target user (id order) → current
/// credential (401) → target user exists (404 E10005) → lock the permission → the locked grant (404) → the scope rule:
/// a scoped permission needs one of its scopes, an unscoped one takes none (400) → unchanged user, permission and scope
/// (400) → ACTIVE user, permission and scope when the request gives the permission (404 E10005) → PLATFORM_ADMIN (403) →
/// duplicate (409) → guarded write. One grant is one permission in one scope.
/// </summary>
public sealed class UpsertUserPermissionCommandHandler(
    CallerCredential credential,
    IUserLockRepository users,
    IPermissionRepository permissions,
    IUserPermissionRepository grants,
    ITransactionRunner transactions) : ICommandHandler<UpsertUserPermissionCommand, UpsertUserPermissionResult>
{
    private const string Active = MasterLifecycle.Active;

    /// <summary>The validated request: Id when it updates an existing grant.</summary>
    private sealed record Request(Guid? Id, Guid UserId, Guid PermissionId, Guid? ScopeId, string RowStatus);

    public async Task<Result<UpsertUserPermissionResult>> HandleAsync(UpsertUserPermissionCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        // The validator has checked every value.
        var q = new Request(r.Id.IsSet ? Guid.Parse(r.Id.Value!) : null, Guid.Parse(r.UserId!), Guid.Parse(r.PermissionId!),
            r.ScopeId.IsSet ? Guid.Parse(r.ScopeId.Value!) : null, r.RowStatus.IsSet ? r.RowStatus.Value! : Active);
        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, q, ct), cancellationToken);
        }
        // The two partial unique indexes (with and without a scope) are the final guards against a concurrent grant.
        catch (DuplicateKeyException duplicate) when (duplicate.ConstraintName.StartsWith("uq_t_user_permission_", StringComparison.Ordinal))
        {
            return AuthorErrors.UserPermissionTaken;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertUserPermissionResult>> RunAsync(Caller caller, Request q, CancellationToken ct)
    {
        var locked = await users.LockUsersAsync(new[] { caller.UserId, q.UserId }.Distinct().ToList(), ct);
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Fail(verified.FirstError);
        if (!locked.TryGetValue(q.UserId, out var userStatus)) return Fail(AuthorErrors.TargetUserNotFound);

        var permission = await permissions.LockAnyAsync(q.PermissionId, ct);
        var grant = q.Id is { } id ? await grants.LockGrantAsync(id, ct) : null;
        if (q.Id is not null && grant is null) return Fail(AuthorErrors.UserPermissionNotFound);

        var definitions = await permissions.DefinitionsAsync(q.PermissionId, ct);
        var definition = definitions.FirstOrDefault(d => d.ScopeId == q.ScopeId);
        if ((ScopeRule(q, definitions, definition) ?? Unchanged(q, grant, definition)) is { } inputError) return Fail(inputError);
        if (Gives(q, grant) && !(userStatus == Active && permission == Active && (definition is null || IsActive(definition))))
            return Fail(AuthorErrors.UserPermissionNotActive);

        if (!caller.IsPlatformAdmin) return Fail(AuthorErrors.AssignPermissionsForbidden);
        if (await grants.TakenAsync(q.UserId, q.PermissionId, definition?.PermissionScopeId, q.Id, ct)) return Fail(AuthorErrors.UserPermissionTaken);
        return await WriteAsync(q, definition?.PermissionScopeId, caller.UserId.ToString(), verified.Value, ct);
    }

    /// <summary>A permission that has scopes needs scopeId, one of them; a permission without scopes takes no scopeId (both 400).</summary>
    private static Error? ScopeRule(Request q, IReadOnlyList<PermissionScopeDefinition> definitions, PermissionScopeDefinition? definition) =>
        definitions.Count > 0 && q.ScopeId is null ? AuthorErrors.ScopeIdRequired
        : q.ScopeId is not null && definition is null ? AuthorErrors.NotAScopeOfThePermission
        : null;

    /// <summary>The user, the permission and the scope of a grant are fixed after the insert.</summary>
    private static Error? Unchanged(Request q, UserPermissionRow? grant, PermissionScopeDefinition? definition) =>
        grant is not null && (grant.UserId != q.UserId || grant.PermissionId != q.PermissionId || grant.PermissionScopeId != definition?.PermissionScopeId)
            ? AuthorErrors.UserPermissionChanged
            : null;

    /// <summary>An insert, or an update that makes a grant ACTIVE again, gives the permission.</summary>
    private static bool Gives(Request q, UserPermissionRow? grant) => grant is null || (q.RowStatus == Active && grant.RowStatus != Active);

    private static bool IsActive(PermissionScopeDefinition d) => d.RowStatus == Active && d.ScopeRowStatus == Active;

    private async Task<Result<UpsertUserPermissionResult>> WriteAsync(Request q, Guid? permissionScopeId, string by, DateTime now, CancellationToken ct)
    {
        var id = q.Id ?? await grants.InsertAsync(q.UserId, q.PermissionId, permissionScopeId, q.RowStatus, by, now, ct);
        if (q.Id is not null && !await grants.UpdateAsync(id, q.RowStatus, by, now, ct)) return Fail(AuthorErrors.UserPermissionNotFound);
        return Result.Success(new UpsertUserPermissionResult(id, q.Id is null ? UpsertAction.Insert : MasterRules.UpdateAction(q.RowStatus)));
    }

    private static Result<UpsertUserPermissionResult> Fail(Error error) => Result.Failure<UpsertUserPermissionResult>(error);
}
