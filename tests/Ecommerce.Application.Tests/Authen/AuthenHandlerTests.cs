using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Entities;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Application.Features.Authen.ResetPassword;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Ecommerce.Application.Tests.Authen;

public class AuthenHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakePasswordRepository _passwords = new();
    private readonly FakeTokenRepository _tokens = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTokenService _tokenService = new();
    private readonly FakeEmailSender _email = new();
    private readonly InlineUnitOfWork _uow = new();

    private IDispatcher Dispatcher()
    {
        var services = new ServiceCollection()
            .AddSingleton<IUserRepository>(_users)
            .AddSingleton<IPasswordHasher>(_hasher)
            .AddSingleton<ITokenService>(_tokenService)
            .AddSingleton<IEmailSender>(_email)
            .AddSingleton<IUnitOfWork>(_uow)
            .AddSingleton<IPasswordRepository>(_passwords)
            .AddSingleton<ITokenRepository>(_tokens)
            .AddSingleton(TimeProvider.System)
            .AddCqrs(AssemblyReference.Assembly);

        return services.BuildServiceProvider().GetRequiredService<IDispatcher>();
    }

    // Register moved to spec 01-register (EF Core, real-database tests); these legacy tests seed the user directly.
    private void SeedUser()
    {
        var name = PersonName.Create("Ittikorn", "S").Value;
        var user = User.Register(
            UserId.New(), Username.Create("Aof").Value, EmailAddress.Create("Aof@Example.com").Value, name, DisplayName.Create(null, name).Value);
        _users.Users.Add(user);
        _passwords.Active[user.Id] = _hasher.Hash("Passw0rdX");
    }

    [Fact]
    public async Task Password_reset_replaces_the_password_consumes_the_token_and_revokes_sessions()
    {
        var dispatcher = Dispatcher();
        SeedUser();
        // The request side moved to spec 03-reset-password; this legacy confirm test seeds the token directly.
        var raw = _tokenService.IssuePasswordResetToken();
        _tokens.Resets.Add(PasswordResetToken.Issue(_users.Users[0].Id, _tokenService.Hash(raw.Value), raw.ExpiresAtUtc));

        var result = await dispatcher.SendAsync(new ResetPasswordCommand(raw.Value, "NewPassw0rd"));

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_users.Users);
        Assert.True(_hasher.Verify("NewPassw0rd", _passwords.Active[user.Id]));
        Assert.Single(_tokens.Consumed);
        Assert.Equal([user.Id], _tokens.RevokedUsers);
    }

    [Fact]
    public async Task Password_reset_rejects_an_unknown_token()
    {
        var result = await Dispatcher().SendAsync(new ResetPasswordCommand("not-a-token", "NewPassw0rd"));

        Assert.Equal(AuthenErrors.InvalidResetToken, result.FirstError);
    }
}
