using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Entities;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Domain;

namespace Ecommerce.Application.Tests.Authen;

internal sealed class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = [];
    public bool ThrowDuplicateOnAdd { get; set; }
    public UserStatus? OverrideStatus { get; set; }

    public Task<bool> UsernameExistsAsync(Username username, CancellationToken ct) =>
        Task.FromResult(Users.Any(u => u.Username == username));

    public Task<bool> EmailExistsAsync(EmailAddress email, CancellationToken ct) =>
        Task.FromResult(Users.Any(u => u.Email == email));

    public Task<User?> FindByIdAsync(UserId id, CancellationToken ct) =>
        Task.FromResult(Users.Where(u => u.Id == id).Select(WithStatus).FirstOrDefault());

    public Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken ct) =>
        Task.FromResult(Users.Where(u => u.Email == email).Select(WithStatus).FirstOrDefault());

    public Task<User?> FindByLoginAsync(LoginIdentifier login, CancellationToken ct) =>
        Task.FromResult(Users.Where(u => u.Username.Value == login.Value || u.Email.Value == login.Value).Select(WithStatus).FirstOrDefault());

    public Task AddAsync(User user, CancellationToken ct)
    {
        if (ThrowDuplicateOnAdd)
        {
            throw new DuplicateKeyException("uq_test");
        }

        Users.Add(user);
        return Task.CompletedTask;
    }

    private User? WithStatus(User user) => OverrideStatus is null
        ? user
        : User.Restore(user.Id, user.Username, user.Email, user.Name, user.DisplayName, user.ProfilePicturePath, OverrideStatus.Value);
}

internal sealed class FakePasswordRepository : IPasswordRepository
{
    public Dictionary<UserId, PasswordCredential> Active { get; } = [];

    public Task<PasswordCredential?> GetActiveAsync(UserId userId, CancellationToken ct) =>
        Task.FromResult(Active.GetValueOrDefault(userId));

    public Task SetAsync(UserId userId, PasswordCredential credential, CancellationToken ct)
    {
        Active[userId] = credential;
        return Task.CompletedTask;
    }
}

internal sealed class FakeTokenRepository : ITokenRepository
{
    public List<byte[]> AccessTokens { get; } = [];
    public List<byte[]> RefreshTokens { get; } = [];
    public List<PasswordResetToken> Resets { get; } = [];
    public List<Guid> Consumed { get; } = [];
    public List<UserId> RevokedUsers { get; } = [];

    public Task SaveAccessTokenAsync(UserId userId, byte[] hash, DateTime expires, CancellationToken ct)
    {
        AccessTokens.Add(hash);
        return Task.CompletedTask;
    }

    public Task SaveRefreshTokenAsync(UserId userId, byte[] hash, DateTime expires, CancellationToken ct)
    {
        RefreshTokens.Add(hash);
        return Task.CompletedTask;
    }

    public Task AddPasswordResetAsync(PasswordResetToken token, CancellationToken ct)
    {
        Resets.Add(token);
        return Task.CompletedTask;
    }

    public Task<PasswordResetToken?> FindActivePasswordResetAsync(byte[] hash, DateTime now, CancellationToken ct) =>
        Task.FromResult(Resets.FirstOrDefault(r => r.TokenHash.SequenceEqual(hash) && r.IsUsableAt(now) && !Consumed.Contains(r.Id)));

    public Task ConsumePasswordResetAsync(PasswordResetToken token, CancellationToken ct)
    {
        Consumed.Add(token.Id);
        return Task.CompletedTask;
    }

    public Task RevokeSessionsAsync(UserId userId, CancellationToken ct)
    {
        RevokedUsers.Add(userId);
        return Task.CompletedTask;
    }
}

/// <summary>"Hash" is the reversed UTF-8 bytes: trivially checkable and not meant to be secure.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int VerifyCalls { get; private set; }

    public PasswordCredential Hash(string password) => new(Reverse(password), [1, 2, 3]);

    public bool Verify(string password, PasswordCredential credential)
    {
        VerifyCalls++;
        return Reverse(password).SequenceEqual(credential.Hash);
    }

    private static byte[] Reverse(string value) => System.Text.Encoding.UTF8.GetBytes(value).Reverse().ToArray();
}

internal sealed class FakeTokenService : ITokenService
{
    private int _counter;
    public string LastResetToken { get; private set; } = string.Empty;

    public IssuedToken IssueAccessToken(UserId userId, Username username) =>
        new($"access-{userId}", DateTime.UtcNow.AddMinutes(15));

    public IssuedToken IssueRefreshToken() => new($"refresh-{++_counter}", DateTime.UtcNow.AddDays(14));

    public IssuedToken IssuePasswordResetToken()
    {
        LastResetToken = $"reset-{++_counter}";
        return new IssuedToken(LastResetToken, DateTime.UtcNow.AddMinutes(30));
    }

    public byte[] Hash(string token) => System.Text.Encoding.UTF8.GetBytes(token);
}

internal sealed class FakeEmailSender : IEmailSender
{
    public List<string> Sent { get; } = [];

    public Task SendPasswordResetAsync(string toEmail, string displayName, string resetToken, CancellationToken ct)
    {
        Sent.Add(toEmail);
        return Task.CompletedTask;
    }
}

internal sealed class InlineUnitOfWork : IUnitOfWork
{
    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct) => work(ct);
}
