using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Infrastructure.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ecommerce.Infrastructure.Security;

internal sealed class TokenService(JwtOptions jwt, SecurityOptions security, TimeProvider clock) : ITokenService
{
    private readonly SigningCredentials _signingCredentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)), SecurityAlgorithms.HmacSha256);

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(jwt.AccessTokenMinutes);

    public IssuedToken IssueAccessToken(AccessTokenClaims claims)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now + AccessTokenLifetime;
        var user = claims.User;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            Expires = expires,
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                ["user"] = new Dictionary<string, object?>
                {
                    ["id"] = user.Id.ToString(), ["username"] = user.Username, ["displayName"] = user.DisplayName, ["email"] = user.EmailAddress
                },
                ["roles"] = claims.Claims.Roles.ToArray(),
                ["permissions"] = claims.Claims.Permissions.ToArray(),
                ["scopes"] = claims.Claims.Scopes.ToArray()
            }
        };

        // The spec payload has iat and exp but no nbf.
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return new IssuedToken(handler.CreateToken(descriptor), expires);
    }

    public IssuedToken IssueRefreshToken() =>
        Opaque(clock.GetUtcNow().UtcDateTime.AddDays(jwt.RefreshTokenDays));

    public IssuedToken IssuePasswordResetToken() =>
        Opaque(clock.GetUtcNow().UtcDateTime.AddMinutes(security.PasswordResetMinutes));

    public byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static IssuedToken Opaque(DateTime expiresAtUtc) =>
        new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), expiresAtUtc);
}
