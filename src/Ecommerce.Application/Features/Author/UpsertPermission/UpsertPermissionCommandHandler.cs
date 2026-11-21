using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Author;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.UpsertPermission;

/// <summary>
/// Orchestrates spec 04 in one READ COMMITTED transaction: caller lock and credential (401) → the locked permission (404)
/// → PLATFORM_ADMIN from the token (403) → <see cref="PermissionWriter"/>: scope masters and owned links (404, 400), then
/// duplicate name, grants and link prechecks (409), writes, Q24 and Q22 (409). Any failure rolls back everything.
/// </summary>
public sealed class UpsertPermissionCommandHandler(
    CallerCredential credential,
    IPermissionRepository permissions,
    PermissionWriter writer,
    ITransactionRunner transactions) : ICommandHandler<UpsertPermissionCommand, UpsertPermissionResult>
{
    private static readonly Dictionary<string, Error> UniqueGuards = new()
    {
        ["uq_m_permission_name"] = AuthorErrors.PermissionNameTaken,
        ["uq_t_permission_scope_permission_id_scope_id"] = AuthorErrors.PermissionScopeTaken
    };

    public async Task<Result<UpsertPermissionResult>> HandleAsync(UpsertPermissionCommand r, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (r.Caller is not { Roles: not null } caller) return AuthenErrors.AccessTokenInvalid;

        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(caller, PermissionItem.Parse(r.Id, r.Name, r.Description, r.RowStatus, r.Scopes), ct), cancellationToken);
        }
        // The two unique indexes are the final guards against a concurrent writer with the same name or link.
        catch (DuplicateKeyException duplicate) when (UniqueGuards.TryGetValue(duplicate.ConstraintName, out var taken))
        {
            return taken;
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertPermissionResult>> RunAsync(Caller caller, PermissionItem item, CancellationToken ct)
    {
        var verified = await credential.VerifyAsync(caller, ct);
        if (verified.IsFailure) return Fail(verified.FirstError);
        if (item.Id is { } id && !await permissions.LockAsync(id, ct)) return Fail(AuthorErrors.PermissionNotFound);
        if (!caller.IsPlatformAdmin) return Fail(AuthorErrors.ManagePermissionsForbidden);

        var locks = await writer.LockLinksAsync([item], ct);
        if (PermissionWriter.CheckLinks(item, locks) is { } linkError) return Fail(linkError);

        var written = await writer.WriteAsync(item, caller.UserId.ToString(), verified.Value, ct);
        return written.IsSuccess ? Result.Success(new UpsertPermissionResult(written.Value.PermissionId, written.Value.Action)) : Fail(written.FirstError);
    }

    private static Result<UpsertPermissionResult> Fail(Error error) => Result.Failure<UpsertPermissionResult>(error);
}
