using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.CheckPermission;

/// <summary>
/// Spec 17: one read of the current credential (401 when the stored token or the user is not ACTIVE or the token expired), then
/// the answer from the calling token's flat permissions and scopes claims, each checked on its own and case-sensitively; no grant
/// table is read and PLATFORM_ADMIN adds nothing. The read runs in a READ ONLY transaction like spec 12 so a timeout answers 408.
/// </summary>
public sealed class CheckPermissionQueryHandler(CallerCredential credential, ITransactionRunner transactions)
    : IQueryHandler<CheckPermissionQuery, CheckPermissionResult>
{
    public async Task<Result<CheckPermissionResult>> HandleAsync(CheckPermissionQuery request, CancellationToken cancellationToken)
    {
        // A missing or malformed roles, permissions or scopes claim is a bad token: 401 before any SQL.
        if (request.Caller is not { Roles: not null, Permissions: { } permissions, Scopes: { } scopes } caller) return AuthenErrors.AccessTokenInvalid;

        var allowed = permissions.Contains(request.Permission!, StringComparer.Ordinal)
                      && (request.Scope is null || scopes.Contains(request.Scope, StringComparer.Ordinal));
        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, async ct =>
            {
                var checkedCredential = await credential.CheckAsync(caller, ct);
                return checkedCredential.IsSuccess
                    ? Result.Success(new CheckPermissionResult(request.Permission!, request.Scope, allowed))
                    : Result.Failure<CheckPermissionResult>(checkedCredential.FirstError);
            }, cancellationToken, readOnly: true);
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }
}
