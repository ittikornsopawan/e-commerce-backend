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
            ? Build(userId, header["Bearer ".Length..], expiresAt)
            : null;
    }

    private static Caller Build(Guid userId, string jwt, DateTime expiresAt) =>
        new(userId, jwt, expiresAt, Names(jwt, "roles"), Names(jwt, "permissions"), Names(jwt, "scopes"));

    private static IReadOnlyList<string>? Names(string jwt, string claim) => CallerClaims.TryGetNames(jwt, claim, out var names) ? names : null;
}
