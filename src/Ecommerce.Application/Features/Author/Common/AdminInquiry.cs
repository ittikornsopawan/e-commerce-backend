using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>
/// The frame of the platform-administrator inquiries (author 03, 06, 09 and later): a missing or malformed roles claim is
/// 401 before any SQL; then one REPEATABLE READ READ ONLY snapshot with no lock or write: the current credential (401),
/// exact PLATFORM_ADMIN from the token (403) before anything is read, then the read; a timeout is 408.
/// </summary>
public sealed class AdminInquiry(CallerCredential credential, ITransactionRunner transactions)
{
    public async Task<Result<T>> RunAsync<T>(Caller? caller, Error forbidden, Func<CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        if (caller is not { Roles: not null } verified) return Result.Failure<T>(AuthenErrors.AccessTokenInvalid);

        try
        {
            return await transactions.RunAsync(IsolationLevel.RepeatableRead, ct => ReadAsync(verified, forbidden, read, ct), cancellationToken, readOnly: true);
        }
        catch (TimeoutException)
        {
            return Result.Failure<T>(CommonErrors.Timeout);
        }
    }

    private async Task<Result<T>> ReadAsync<T>(Caller caller, Error forbidden, Func<CancellationToken, Task<T>> read, CancellationToken ct)
    {
        var checkedCredential = await credential.CheckAsync(caller, ct);
        if (checkedCredential.IsFailure) return Result.Failure<T>(checkedCredential.FirstError);

        // The author masters describe the security model: refused before any read, so the answer never shows whether a row exists.
        if (!caller.IsPlatformAdmin) return Result.Failure<T>(forbidden);
        return Result.Success(await read(ct));
    }
}
