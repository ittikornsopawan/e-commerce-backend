using Ecommerce.Application.Common;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>One validated link: Id when it updates an existing one.</summary>
public sealed record PermissionLink(Guid? Id, Guid ScopeId, string RowStatus)
{
    public bool Deleted => RowStatus == MasterLifecycle.Deleted;
}

/// <summary>One validated permission with its links: Id when it updates an existing one.</summary>
public sealed record PermissionItem(Guid? Id, PermissionValues Values, IReadOnlyList<PermissionLink> Links)
{
    /// <summary>The validator has checked every value.</summary>
    public static PermissionItem Parse(
        Optional<string?> id, string? name, string? description, Optional<string?> rowStatus, IReadOnlyList<ScopeLinkInput> scopes) => new(
        id.IsSet ? Guid.Parse(id.Value!) : null,
        new PermissionValues(name!, description!, rowStatus.IsSet ? rowStatus.Value! : MasterLifecycle.Active),
        scopes.Select(s => new PermissionLink(
            s.Id.IsSet ? Guid.Parse(s.Id.Value!) : null,
            Guid.Parse(s.ScopeId!),
            s.RowStatus.IsSet ? s.RowStatus.Value! : MasterLifecycle.Active)).ToList());
}

/// <summary>The ACTIVE scope masters and the owned links locked for a request.</summary>
public sealed record LinkLocks(IReadOnlySet<Guid> ActiveScopes, IReadOnlyDictionary<Guid, LinkRow> Links);

/// <summary>A written permission and what happened to it.</summary>
public sealed record PermissionWritten(Guid PermissionId, UpsertAction Action);

/// <summary>
/// The permission write of specs 04 and 05, after the permission lock and the PLATFORM_ADMIN check, inside the caller's
/// transaction: lock the ACTIVE masters and the owned links (once for the request), check one item's links (404, 400),
/// then for that item: duplicate name, grants and link prechecks (409), parent and link writes, Q24 and Q22 (409).
/// </summary>
public sealed class PermissionWriter(IPermissionRepository permissions)
{
    /// <summary>Locks the ACTIVE masters of every ACTIVE link, then the owned links of every target permission, both in id order.</summary>
    public async Task<LinkLocks> LockLinksAsync(IReadOnlyList<PermissionItem> items, CancellationToken ct)
    {
        var links = items.SelectMany(i => i.Links).ToList();
        var active = await permissions.LockActiveScopesAsync(links.Where(l => !l.Deleted).Select(l => l.ScopeId).ToList(), ct);
        var targets = items.Where(i => i.Id is not null).Select(i => i.Id!.Value).ToList();
        var linkIds = links.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToList();
        var owned = targets.Count > 0 ? await permissions.LockLinksAsync(targets, linkIds, ct) : new Dictionary<Guid, LinkRow>();
        return new LinkLocks(active, owned);
    }

    /// <summary>An ACTIVE link needs an ACTIVE master (Q23 lets a removal name a closed one); a link id must be this permission's, its scope unchanged.</summary>
    public static Error? CheckLinks(PermissionItem item, LinkLocks locks)
    {
        foreach (var link in item.Links)
        {
            if (!link.Deleted && !locks.ActiveScopes.Contains(link.ScopeId)) return AuthorErrors.ScopeNotFound;
            if (link.Id is not { } linkId) continue;
            if (!locks.Links.TryGetValue(linkId, out var stored) || stored.PermissionId != item.Id) return AuthorErrors.PermissionScopeNotFound;
            if (stored.ScopeId != link.ScopeId) return AuthorErrors.ScopeIdChanged;
        }

        return null;
    }

    /// <summary>The item's checks, writes and final Q24/Q22 checks; its links were checked by <see cref="CheckLinks"/>.</summary>
    public async Task<Result<PermissionWritten>> WriteAsync(PermissionItem item, string by, DateTime now, CancellationToken ct)
    {
        // Every non-DELETED link before the writes, for Q22 after them.
        var before = item.Id is { } existing ? await permissions.CountLinksAsync(existing, ct) : 0;
        if (await ConflictAsync(item, ct) is { } conflict) return Result.Failure<PermissionWritten>(conflict);

        var written = await WriteRowsAsync(item, by, now, ct);
        if (written.IsFailure) return Result.Failure<PermissionWritten>(written.FirstError);
        if (await FinalCheckAsync(item, written.Value, before, ct) is { } final) return Result.Failure<PermissionWritten>(final);

        var action = item.Id is null ? UpsertAction.Insert : MasterRules.UpdateAction(item.Values.RowStatus);
        return Result.Success(new PermissionWritten(written.Value, action));
    }

    /// <summary>Duplicate name; grants before a delete or archive; then each link's duplicate and, for a removal, its use. All before any write.</summary>
    private async Task<Error?> ConflictAsync(PermissionItem item, CancellationToken ct)
    {
        if (await permissions.NameTakenAsync(item.Values.Name, item.Id, ct)) return AuthorErrors.PermissionNameTaken;
        if (item.Id is not { } id) return null; // a new permission has no grant and no link yet

        if (MasterLifecycle.TakesOutOfUse(item.Values.RowStatus) && await permissions.GrantedAsync(id, ct)) return AuthorErrors.PermissionInUse;
        foreach (var link in item.Links)
        {
            if (await permissions.LinkTakenAsync(id, link.ScopeId, link.Id, ct)) return AuthorErrors.PermissionScopeTaken;
            if (link.Deleted && await permissions.LinkInUseAsync(link.Id!.Value, ct)) return AuthorErrors.PermissionScopeInUse;
        }

        return null;
    }

    private async Task<Result<Guid>> WriteRowsAsync(PermissionItem item, string by, DateTime now, CancellationToken ct)
    {
        var id = item.Id ?? await permissions.InsertAsync(item.Values, by, now, ct);
        if (item.Id is not null && !await permissions.UpdateAsync(id, item.Values, by, now, ct)) return Result.Failure<Guid>(AuthorErrors.PermissionNotFound);

        foreach (var link in item.Links)
        {
            if (link.Id is null) await permissions.InsertLinkAsync(id, link.ScopeId, link.RowStatus, by, now, ct);
            else if (!await permissions.UpdateLinkAsync(link.Id.Value, id, link.ScopeId, link.RowStatus, by, now, ct))
                return Result.Failure<Guid>(AuthorErrors.PermissionScopeNotFound);
        }

        return Result.Success(id);
    }

    /// <summary>On the final link count: Q24 (delete needs zero links) then Q22 (a granted permission cannot lose its last link).</summary>
    private async Task<Error?> FinalCheckAsync(PermissionItem item, Guid id, long before, CancellationToken ct)
    {
        var final = await permissions.CountLinksAsync(id, ct);
        if (item.Values.RowStatus == MasterLifecycle.Deleted && final > 0) return AuthorErrors.PermissionScopesRemain;
        if (before > 0 && final == 0 && await permissions.GrantedAsync(id, ct)) return AuthorErrors.PermissionWouldBeUnscoped;
        return null;
    }
}
