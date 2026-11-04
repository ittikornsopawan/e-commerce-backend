using System.Buffers.Text;
using System.Security.Claims;
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

    public IssuedToken IssueAccessToken(UserId userId, Username username)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = _signingCredentials,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, username.Value),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ])
        };

        return new IssuedToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public IssuedToken IssueRefreshToken() =>
        Opaque(clock.GetUtcNow().UtcDateTime.AddDays(jwt.RefreshTokenDays));

    public IssuedToken IssuePasswordResetToken() =>
        Opaque(clock.GetUtcNow().UtcDateTime.AddMinutes(security.PasswordResetMinutes));

    public byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static IssuedToken Opaque(DateTime expiresAtUtc) =>
        new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)), expiresAtUtc);
}
