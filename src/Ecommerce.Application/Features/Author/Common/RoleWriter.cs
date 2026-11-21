using Ecommerce.Application.Common;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>A written role and what happened to it.</summary>
public sealed record RoleWritten(Guid RoleId, UpsertAction Action);

/// <summary>
/// The role write of specs 01 and 02, after the target lock and the PLATFORM_ADMIN check: duplicate name (409) for an
/// insert; system role, duplicate name and in use before DELETED/ARCHIVED (409), then the guarded update, for an update.
/// Runs inside the caller's transaction, so a batch item sees the writes of the items before it.
/// </summary>
public sealed class RoleWriter(IRoleRepository roles)
{
    public async Task<Result<RoleWritten>> InsertAsync(RoleValues values, string by, DateTime now, CancellationToken ct)
    {
        if (await roles.NameTakenAsync(values.Name, null, ct)) return Result.Failure<RoleWritten>(AuthorErrors.RoleNameTaken);

        var id = await roles.InsertAsync(values, by, now, ct);
        return Result.Success(new RoleWritten(id, UpsertAction.Insert));
    }

    /// <param name="storedName">The name read with the target lock: the stored name decides the system role, not the proposed one.</param>
    public async Task<Result<RoleWritten>> UpdateAsync(
        Guid id, string storedName, RoleValues values, string by, DateTime now, CancellationToken ct)
    {
        if (storedName == RoleCode.PlatformAdmin) return Result.Failure<RoleWritten>(AuthorErrors.SystemRoleLocked);
        if (await roles.NameTakenAsync(values.Name, id, ct)) return Result.Failure<RoleWritten>(AuthorErrors.RoleNameTaken);
        if (MasterLifecycle.TakesOutOfUse(values.RowStatus) && await roles.InUseAsync(id, ct))
            return Result.Failure<RoleWritten>(AuthorErrors.RoleInUse);

        if (!await roles.UpdateAsync(id, values, by, now, ct)) return Result.Failure<RoleWritten>(AuthorErrors.RoleNotFound);
        return Result.Success(new RoleWritten(id, ActionOf(values.RowStatus)));
    }

    private static UpsertAction ActionOf(string rowStatus) => rowStatus switch
    {
        MasterLifecycle.Deleted => UpsertAction.Delete,
        MasterLifecycle.Archived => UpsertAction.Archive,
        _ => UpsertAction.Update
    };
}
