using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Authen.Repositories;

/// <summary>The reads and writes of spec 02-login, in its lock order, inside one READ COMMITTED transaction.</summary>
public interface ILoginRepository
{
    /// <summary>QUERY - Serialize identifier: a transaction lock on the supplied identifier, held until commit.</summary>
    Task LockIdentifierAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>QUERY - Count failed attempts of this exact identifier since <paramref name="windowStartUtc"/>.</summary>
    Task<int> CountRecentFailuresAsync(string identifier, DateTime windowStartUtc, CancellationToken cancellationToken);

    /// <summary>QUERY - Lock the user: ACTIVE users whose username or email is exactly the identifier, FOR UPDATE by id.</summary>
    Task<IReadOnlyList<LoginUser>> LockActiveUsersAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>QUERY - Get current password: the ACTIVE PASSWORD row, or null.</summary>
    Task<PasswordCredential?> GetCurrentPasswordAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The role, permission and scope-definition queries of the spec, in their order.</summary>
    Task<AuthorizationFacts> GetAuthorizationFactsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>QUERY - Get active scopes of the assigned scope ids.</summary>
    Task<IReadOnlyList<ScopeFact>> GetActiveScopesAsync(IReadOnlyList<Guid> scopeIds, CancellationToken cancellationToken);

    /// <summary>INSERT - Create access token, then Create refresh token.</summary>
    Task AddSessionTokensAsync(LoginSession session, CancellationToken cancellationToken);

    /// <summary>INSERT - Record attempt.</summary>
    Task RecordAttemptAsync(string identifier, Guid? userId, LoginResult result, DateTime nowUtc, CancellationToken cancellationToken);
}
