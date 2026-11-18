using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen.Domain;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Application.Features.Authen.Common;

/// <summary>
/// The shared tail of login (spec 02) and refresh (spec 05): resolve the current claims with the login queries, sign a
/// fresh JWT and refresh token, and store both hashes in one session. Used inside the caller's transaction.
/// </summary>
public sealed class SessionTokens(ILoginRepository logins, ITokenService tokenService)
{
    public async Task<TokenPairResponse> IssueAsync(LoginUser user, Guid sessionId, CancellationToken cancellationToken)
    {
        var facts = await logins.GetAuthorizationFactsAsync(user.Id, cancellationToken);
        var scopes = await logins.GetActiveScopesAsync(AuthorizationClaims.AssignedScopeIds(facts), cancellationToken);

        var access = tokenService.IssueAccessToken(new AccessTokenClaims(user, AuthorizationClaims.Resolve(facts, scopes)));
        var refresh = tokenService.IssueRefreshToken();
        await logins.AddSessionTokensAsync(new LoginSession(
            user.Id, sessionId, tokenService.Hash(access.Value), access.ExpiresAtUtc, tokenService.Hash(refresh.Value), refresh.ExpiresAtUtc), cancellationToken);

        return new TokenPairResponse("Bearer", access.Value, refresh.Value, (int)tokenService.AccessTokenLifetime.TotalSeconds);
    }
}
