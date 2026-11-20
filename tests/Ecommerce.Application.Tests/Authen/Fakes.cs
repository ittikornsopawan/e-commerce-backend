using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen.ValueObjects;

namespace Ecommerce.Application.Tests.Authen;

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

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(15);

    public IssuedToken IssueAccessToken(AccessTokenClaims claims) =>
        new($"access-{claims.User.Id}-{++_counter}", DateTime.UtcNow.AddMinutes(15));

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
    public List<(string To, string Link)> Sent { get; } = [];

    public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken ct)
    {
        Sent.Add((toEmail, resetLink));
        return Task.CompletedTask;
    }
}
