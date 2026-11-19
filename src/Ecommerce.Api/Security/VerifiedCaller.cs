using Ecommerce.Application.Features.Authen.Common;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Security;

/// <summary>
/// The verified JWT of a protected call, read in the action instead of [Authorize] so a 400 answers before a 401
/// (specs 06 to 10). Null when there is no Bearer token or it did not verify.
/// </summary>
public static class VerifiedCaller
{
    public static Caller? Of(ControllerBase controller)
    {
        var header = controller.Request.Headers.Authorization.ToString();
        var user = controller.User;
        return user.Identity?.IsAuthenticated == true && header.StartsWith("Bearer ", StringComparison.Ordinal)
               && user.TryGetUserId(out var userId) && user.TryGetExpiresAt(out var expiresAt)
            ? new Caller(userId, header["Bearer ".Length..], expiresAt)
            : null;
    }
}
