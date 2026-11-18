using System.Security.Claims;

namespace Ecommerce.Api.Security;

/// <summary>Reads the verified JWT. Roles come only from the <c>roles</c> claim, never from the author tables.</summary>
public static class CallerClaims
{
    public const string PlatformAdmin = "PLATFORM_ADMIN";

    public static bool TryGetUserId(this ClaimsPrincipal user, out Guid userId) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out userId);

    // Exact, case-sensitive match (handoff section 2).
    public static bool IsPlatformAdmin(this ClaimsPrincipal user) =>
        user.FindAll("roles").Any(c => c.Value == PlatformAdmin);
}
