using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.Me;

/// <summary>
/// Spec 12: one read of the current credential (401 when the stored token or the user is not ACTIVE or the token expired),
/// then the calling token's role snapshot; roles are never looked up. The read runs in a READ ONLY transaction, the same
/// single statement as the spec's autocommit SELECT, so a timeout answers 408 through the transaction runner.
/// </summary>
public sealed class MeQueryHandler(CallerCredential credential, ITransactionRunner transactions) : IQueryHandler<MeQuery, MeResult>
{
    public async Task<Result<MeResult>> HandleAsync(MeQuery request, CancellationToken cancellationToken)
    {
        // A missing or malformed roles claim is a bad token: 401 before any SQL.
        if (request.Caller is not { Roles: { } roles } caller) return AuthenErrors.AccessTokenInvalid;

        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, async ct =>
            {
                var checkedCredential = await credential.CheckAsync(caller, ct);
                return checkedCredential.IsSuccess
                    ? Result.Success(new MeResult(caller.UserId, caller.IsPlatformAdmin, roles))
                    : Result.Failure<MeResult>(checkedCredential.FirstError);
            }, cancellationToken, readOnly: true);
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }
}
