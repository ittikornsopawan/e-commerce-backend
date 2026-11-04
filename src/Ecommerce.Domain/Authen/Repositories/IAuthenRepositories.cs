using Ecommerce.Domain.Authen.Entities;
using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Domain.Authen.Repositories;

public interface IUserRepository
{
    Task<bool> UsernameExistsAsync(Username username, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(EmailAddress email, CancellationToken cancellationToken);

    Task<User?> FindByIdAsync(UserId id, CancellationToken cancellationToken);

    Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken);

    Task<User?> FindByLoginAsync(LoginIdentifier login, CancellationToken cancellationToken);

    /// <exception cref="DuplicateKeyException">The username or email is already taken.</exception>
    Task AddAsync(User user, CancellationToken cancellationToken);
}

public interface IPasswordRepository
{
    Task<PasswordCredential?> GetActiveAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>Makes <paramref name="credential"/> the user's only active password.</summary>
    Task SetAsync(UserId userId, PasswordCredential credential, CancellationToken cancellationToken);
}

public interface ITokenRepository
{
    Task SaveAccessTokenAsync(UserId userId, byte[] tokenHash, DateTime expiresAtUtc, CancellationToken cancellationToken);

    Task SaveRefreshTokenAsync(UserId userId, byte[] tokenHash, DateTime expiresAtUtc, CancellationToken cancellationToken);

    /// <summary>Stores a new reset token and invalidates the user's earlier unused ones.</summary>
    Task AddPasswordResetAsync(PasswordResetToken token, CancellationToken cancellationToken);

    Task<PasswordResetToken?> FindActivePasswordResetAsync(byte[] tokenHash, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Saves that <paramref name="token"/> has been consumed.</summary>
    Task ConsumePasswordResetAsync(PasswordResetToken token, CancellationToken cancellationToken);

    /// <summary>Invalidates all of the user's access and refresh tokens.</summary>
    Task RevokeSessionsAsync(UserId userId, CancellationToken cancellationToken);
}
