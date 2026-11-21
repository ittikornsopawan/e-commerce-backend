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

namespace Ecommerce.Application.Features.Author.UpsertPermission;

/// <summary>
/// Orchestrates spec 04 in one READ COMMITTED transaction: caller lock and credential (401) → the locked permission (404)
/// → PLATFORM_ADMIN from the token (403) → scope masters and owned links (404, 400) → duplicate name, grants and link
/// prechecks (409) → parent and link writes → Q24 and Q22 on the final link count (409). Any failure rolls back everything.
/// </summary>
public sealed class UpsertPermissionCommandHandler(
    CallerCredential credential,
    IPermissionRepository permissions,
    ITransactionRunner transactions) : ICommandHandler<UpsertPermissionCommand, UpsertPermissionResult>
{
    /// <summary>One validated link: id when it updates an existing one.</summary>
    private sealed record Link(Guid? Id, Guid ScopeId, string RowStatus)
    {
        public bool Deleted => RowStatus == MasterLifecycle.Deleted;
    }

    /// <summary>The validated request: Id when it updates an existing permission.</summary>
    private sealed record Request(Guid? Id, PermissionValues Values, IReadOnlyList<Link> Links);

    public async Task<Result<UpsertPermissionResult>> HandleAsync(UpsertPermissionCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, Parse(r), ct), cancellationToken);
        }
        // The two unique indexes are the final guards against a concurrent writer with the same name or link.
        catch (DuplicateKeyException duplicate) when (duplicate.ConstraintName == "uq_m_permission_name")
        {
            return AuthorErrors.PermissionNameTaken;
        }
        catch (DuplicateKeyException duplicate) when (duplicate.ConstraintName == "uq_t_permission_scope_permission_id_scope_id")
        {
            return AuthorErrors.PermissionScopeTaken;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertPermissionResult>> RunAsync(Caller caller, Request q, CancellationToken ct)
    {
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Fail(verified.FirstError);
        if (q.Id is { } id && !await permissions.LockAsync(id, ct)) return Fail(AuthorErrors.PermissionNotFound);
        if (!caller.IsPlatformAdmin) return Fail(AuthorErrors.ManagePermissionsForbidden);
        if (await CheckLinksAsync(q, ct) is { } linkError) return Fail(linkError);

        // Every non-DELETED link before the writes, for Q22 after them.
        var before = q.Id is { } existing ? await permissions.CountLinksAsync(existing, ct) : 0;
        if (await ConflictAsync(q, ct) is { } conflict) return Fail(conflict);

        var written = await WriteAsync(q, caller.UserId.ToString(), verified.Value, ct);
        if (written.IsFailure) return Fail(written.FirstError);
        if (await FinalCheckAsync(q, written.Value, before, ct) is { } final) return Fail(final);

        var action = q.Id is null ? UpsertAction.Insert : MasterRules.UpdateAction(q.Values.RowStatus);
        return Result.Success(new UpsertPermissionResult(written.Value, action));
    }

    private static Result<UpsertPermissionResult> Fail(Error error) => Result.Failure<UpsertPermissionResult>(error);

    /// <summary>An ACTIVE link needs an ACTIVE master (Q23 lets a removal name a closed one); a link id must be this permission's, its scope unchanged.</summary>
    private async Task<Error?> CheckLinksAsync(Request q, CancellationToken ct)
    {
        var active = await permissions.LockActiveScopesAsync(q.Links.Where(l => !l.Deleted).Select(l => l.ScopeId).ToList(), ct);
        var linkIds = q.Links.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToList();
        var owned = q.Id is { } id ? await permissions.LockLinksAsync(id, linkIds, ct) : new Dictionary<Guid, Guid>();

        foreach (var link in q.Links)
        {
            if (!link.Deleted && !active.Contains(link.ScopeId)) return AuthorErrors.ScopeNotFound;
            if (link.Id is not { } linkId) continue;
            if (!owned.TryGetValue(linkId, out var storedScope)) return AuthorErrors.PermissionScopeNotFound;
            if (storedScope != link.ScopeId) return AuthorErrors.ScopeIdChanged;
        }

        return null;
    }

    /// <summary>Duplicate name; grants before a delete or archive; then each link's duplicate and, for a removal, its use. All before any write.</summary>
    private async Task<Error?> ConflictAsync(Request q, CancellationToken ct)
    {
        if (await permissions.NameTakenAsync(q.Values.Name, q.Id, ct)) return AuthorErrors.PermissionNameTaken;
        if (q.Id is not { } id) return null; // a new permission has no grant and no link yet

        if (MasterLifecycle.TakesOutOfUse(q.Values.RowStatus) && await permissions.GrantedAsync(id, ct)) return AuthorErrors.PermissionInUse;
        foreach (var link in q.Links)
        {
            if (await permissions.LinkTakenAsync(id, link.ScopeId, link.Id, ct)) return AuthorErrors.PermissionScopeTaken;
            if (link.Deleted && await permissions.LinkInUseAsync(link.Id!.Value, ct)) return AuthorErrors.PermissionScopeInUse;
        }

        return null;
    }

    private async Task<Result<Guid>> WriteAsync(Request q, string by, DateTime now, CancellationToken ct)
    {
        var id = q.Id ?? await permissions.InsertAsync(q.Values, by, now, ct);
        if (q.Id is not null && !await permissions.UpdateAsync(id, q.Values, by, now, ct)) return Result.Failure<Guid>(AuthorErrors.PermissionNotFound);

        foreach (var link in q.Links)
        {
            if (link.Id is null) await permissions.InsertLinkAsync(id, link.ScopeId, link.RowStatus, by, now, ct);
            else if (!await permissions.UpdateLinkAsync(link.Id.Value, id, link.ScopeId, link.RowStatus, by, now, ct))
                return Result.Failure<Guid>(AuthorErrors.PermissionScopeNotFound);
        }

        return Result.Success(id);
    }

    /// <summary>On the final link count: Q24 (delete needs zero links) then Q22 (a granted permission cannot lose its last link).</summary>
    private async Task<Error?> FinalCheckAsync(Request q, Guid id, long before, CancellationToken ct)
    {
        var final = await permissions.CountLinksAsync(id, ct);
        if (q.Values.RowStatus == MasterLifecycle.Deleted && final > 0) return AuthorErrors.PermissionScopesRemain;
        if (before > 0 && final == 0 && await permissions.GrantedAsync(id, ct)) return AuthorErrors.PermissionWouldBeUnscoped;
        return null;
    }

    /// <summary>The validator has checked every value.</summary>
    private static Request Parse(UpsertPermissionCommand r) => new(
        r.Id.IsSet ? Guid.Parse(r.Id.Value!) : null,
        new PermissionValues(r.Name!, r.Description!, r.RowStatus.IsSet ? r.RowStatus.Value! : MasterLifecycle.Active),
        r.Scopes.Select(s => new Link(
            s.Id.IsSet ? Guid.Parse(s.Id.Value!) : null,
            Guid.Parse(s.ScopeId!),
            s.RowStatus.IsSet ? s.RowStatus.Value! : MasterLifecycle.Active)).ToList());
}
