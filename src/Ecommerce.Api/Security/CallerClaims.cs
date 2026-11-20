using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace Ecommerce.Api.Security;

/// <summary>Reads the verified JWT. Roles come only from the <c>roles</c> claim, never from the author tables.</summary>
public static class CallerClaims
{
    public static bool TryGetUserId(this ClaimsPrincipal user, out Guid userId) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out userId);

    /// <summary>The verified <c>exp</c> claim as UTC.</summary>
    public static bool TryGetExpiresAt(this ClaimsPrincipal user, out DateTime expiresAtUtc)
    {
        var ok = long.TryParse(user.FindFirst("exp")?.Value, out var seconds);
        expiresAtUtc = ok ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : default;
        return ok;
    }

    /// <summary>
    /// The <c>roles</c> claim of an already verified JWT, read from its payload because the claims principal cannot tell
    /// a missing claim from an empty array. Valid only as an array of unique non-empty strings (JWT payload of 2026-11-13).
    /// </summary>
    public static bool TryGetRoles(string verifiedJwt, out IReadOnlyList<string> roles)
    {
        roles = [];
        var parts = verifiedJwt.Split('.');
        if (parts.Length != 3) return false;

        using var payload = JsonDocument.Parse(Base64UrlEncoder.Decode(parts[1]));
        if (!payload.RootElement.TryGetProperty("roles", out var claim) || claim.ValueKind != JsonValueKind.Array) return false;

        var names = claim.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null).ToList();
        if (names.Any(string.IsNullOrEmpty) || names.Distinct(StringComparer.Ordinal).Count() != names.Count) return false;

        roles = names!;
        return true;
    }
}
