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

namespace Ecommerce.Application.Features.Author.UpsertRolePermission;

/// <summary>
/// Orchestrates spec 13 in one READ COMMITTED transaction: caller lock and credential (401) → lock the role, then the
/// permission → the locked grant (404) with its fixed role and permission (400) → ACTIVE role and permission when the
/// request gives the permission (404 E10005) → the permission's scope definitions and the grant's locked scope links: each
/// item's link (404), unchanged scope (400), a scope of the permission (400), an ACTIVE given scope (404 E10005) →
/// PLATFORM_ADMIN (403) → duplicate grant and scope (409) → writes → an ACTIVE grant of a scoped permission keeps an ACTIVE
/// scope (400). Every rejection after BEGIN rolls back.
/// </summary>
public sealed class UpsertRolePermissionCommandHandler(
    CallerCredential credential,
    IRoleRepository roles,
    IPermissionRepository permissions,
    IRolePermissionRepository grants,
    ITransactionRunner transactions) : ICommandHandler<UpsertRolePermissionCommand, UpsertRolePermissionResult>
{
    private const string Active = MasterLifecycle.Active;

    /// <summary>The validated request: Id when it updates an existing grant.</summary>
    private sealed record Request(Guid? Id, Guid RoleId, Guid PermissionId, string RowStatus, IReadOnlyList<PermissionLink> Scopes);

    private static readonly Dictionary<string, Error> UniqueGuards = new()
    {
        ["uq_t_role_permission_role_id_permission_id"] = AuthorErrors.RolePermissionTaken,
        ["uq_t_role_permission_scope_role_permission_scope"] = AuthorErrors.RolePermissionScopeTaken
    };

    public async Task<Result<UpsertRolePermissionResult>> HandleAsync(UpsertRolePermissionCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        var q = Parse(r);
        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, q, ct), cancellationToken);
        }
        // The unique indexes are the final guards against a concurrent grant or scope of the same pair.
        catch (DuplicateKeyException duplicate) when (UniqueGuards.TryGetValue(duplicate.ConstraintName, out var taken))
        {
            return taken;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertRolePermissionResult>> RunAsync(Caller caller, Request q, CancellationToken ct)
    {
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Fail(verified.FirstError);

        var role = await roles.LockAnyAsync(q.RoleId, ct);
        var permission = await permissions.LockAnyAsync(q.PermissionId, ct);
        var grant = q.Id is { } id ? await grants.LockGrantAsync(id, ct) : null;
        if (CheckGrant(q, grant) is { } grantError) return Fail(grantError);
        if (GivesPermission(q, grant) && (role?.RowStatus != Active || permission != Active)) return Fail(AuthorErrors.RoleOrPermissionNotActive);

        var definitions = await grants.DefinitionsAsync(q.PermissionId, ct);
        var links = q.Id is { } grantId ? await grants.LockScopesAsync(grantId, ct) : [];
        if ((CheckLinks(q, definitions, links) ?? CheckScopes(q, definitions)) is { } scopeError) return Fail(scopeError);

        if (!caller.IsPlatformAdmin) return Fail(AuthorErrors.AssignPermissionsForbidden);
        if (await grants.TakenAsync(q.RoleId, q.PermissionId, q.Id, ct)) return Fail(AuthorErrors.RolePermissionTaken);
        if (DuplicateScope(q, definitions, links) is { } duplicate) return Fail(duplicate);
        return await WriteAsync(q, definitions, caller.UserId.ToString(), verified.Value, ct);
    }

    /// <summary>An id names a non-DELETED grant (404) of the same role and permission (400): both are fixed after the insert.</summary>
    private static Error? CheckGrant(Request q, RolePermissionRow? grant) => q.Id switch
    {
        null => null,
        _ when grant is null => AuthorErrors.RolePermissionNotFound,
        _ when grant.RoleId != q.RoleId || grant.PermissionId != q.PermissionId => AuthorErrors.RolePermissionChanged,
        _ => null
    };

    /// <summary>An insert, or an update that makes a grant ACTIVE again, gives the permission.</summary>
    private static bool GivesPermission(Request q, RolePermissionRow? grant) => grant is null || (q.RowStatus == Active && grant.RowStatus != Active);

    /// <summary>Each item with an id names a non-DELETED link of this grant (404) whose definition keeps its scope (400).</summary>
    private static Error? CheckLinks(Request q, IReadOnlyList<PermissionScopeDefinition> definitions, IReadOnlyList<GrantScopeRow> links)
    {
        for (var i = 0; i < q.Scopes.Count; i++)
        {
            if (q.Scopes[i].Id is not { } linkId) continue;
            if (links.FirstOrDefault(l => l.Id == linkId) is not { } link) return AuthorErrors.RolePermissionScopeNotFound.At(Position(i));
            if (definitions.FirstOrDefault(d => d.PermissionScopeId == link.PermissionScopeId)?.ScopeId != q.Scopes[i].ScopeId)
                return AuthorErrors.GrantScopeChanged.At(Position(i));
        }

        return null;
    }

    /// <summary>Every scopeId is a scope of the permission (400); a given scope (a new ACTIVE link) needs an ACTIVE definition and master (404 E10005).</summary>
    private static Error? CheckScopes(Request q, IReadOnlyList<PermissionScopeDefinition> definitions)
    {
        var missing = q.Scopes.ToList().FindIndex(s => definitions.All(d => d.ScopeId != s.ScopeId));
        if (missing >= 0) return AuthorErrors.NotAScopeOfThePermission.At(Position(missing));

        var closed = q.Scopes.ToList().FindIndex(s => s.Id is null && !s.Deleted
            && definitions.First(d => d.ScopeId == s.ScopeId) is var d && (d.RowStatus != Active || d.ScopeRowStatus != Active));
        return closed >= 0 ? AuthorErrors.GrantScopeNotActive.At(Position(closed)) : null;
    }

    /// <summary>A new link to a definition the grant already links (and this request does not remove) is a duplicate.</summary>
    private static Error? DuplicateScope(Request q, IReadOnlyList<PermissionScopeDefinition> definitions, IReadOnlyList<GrantScopeRow> links)
    {
        var removed = q.Scopes.Where(s => s.Deleted && s.Id is not null).Select(s => s.Id!.Value).ToHashSet();
        var duplicate = q.Scopes.ToList().FindIndex(s => s.Id is null && links.Any(l => !removed.Contains(l.Id)
            && l.PermissionScopeId == definitions.First(d => d.ScopeId == s.ScopeId).PermissionScopeId));
        return duplicate >= 0 ? AuthorErrors.RolePermissionScopeTaken.At(Position(duplicate)) : null;
    }

    private async Task<Result<UpsertRolePermissionResult>> WriteAsync(
        Request q, IReadOnlyList<PermissionScopeDefinition> definitions, string by, DateTime now, CancellationToken ct)
    {
        var grantId = q.Id ?? await grants.InsertAsync(q.RoleId, q.PermissionId, q.RowStatus, by, now, ct);
        if (q.Id is not null && !await grants.UpdateAsync(grantId, q.RowStatus, by, now, ct)) return Fail(AuthorErrors.RolePermissionNotFound);
        if (await WriteScopesAsync(q, grantId, definitions, by, now, ct) is { } scopeError) return Fail(scopeError);

        // Deleting the grant deletes its scopes; an ACTIVE grant of a scoped permission must keep an ACTIVE scope.
        if (q.RowStatus == MasterLifecycle.Deleted) await grants.DeleteScopesAsync(grantId, by, now, ct);
        else if (q.RowStatus == Active && definitions.Count > 0 && await grants.ActiveScopesAsync(grantId, ct) == 0) return Fail(AuthorErrors.ScopedGrantNeedsScope);

        var action = q.Id is null ? UpsertAction.Insert : MasterRules.UpdateAction(q.RowStatus);
        return Result.Success(new UpsertRolePermissionResult(grantId, action));
    }

    private async Task<Error?> WriteScopesAsync(Request q, Guid grantId, IReadOnlyList<PermissionScopeDefinition> definitions, string by, DateTime now, CancellationToken ct)
    {
        for (var i = 0; i < q.Scopes.Count; i++)
        {
            var item = q.Scopes[i];
            if (item.Id is null)
                await grants.InsertScopeAsync(grantId, definitions.First(d => d.ScopeId == item.ScopeId).PermissionScopeId, item.RowStatus, by, now, ct);
            else if (!await grants.UpdateScopeAsync(item.Id.Value, grantId, item.RowStatus, by, now, ct))
                return AuthorErrors.RolePermissionScopeNotFound.At(Position(i));
        }

        return null;
    }

    private static string Position(int index) => $"scopes[{index}]";

    /// <summary>The validator has checked every value.</summary>
    private static Request Parse(UpsertRolePermissionCommand r) => new(
        r.Id.IsSet ? Guid.Parse(r.Id.Value!) : null, Guid.Parse(r.RoleId!), Guid.Parse(r.PermissionId!),
        r.RowStatus.IsSet ? r.RowStatus.Value! : Active,
        PermissionLink.ParseAll(r.Scopes));

    private static Result<UpsertRolePermissionResult> Fail(Error error) => Result.Failure<UpsertRolePermissionResult>(error);
}
