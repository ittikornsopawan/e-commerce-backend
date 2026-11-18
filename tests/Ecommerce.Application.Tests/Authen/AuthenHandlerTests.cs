using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Entities;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Application.Features.Authen.Login;
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
    public async Task Login_issues_and_stores_tokens_for_the_right_password()
    {
        var dispatcher = Dispatcher();
        SeedUser();

        var result = await dispatcher.SendAsync(new LoginCommand("aof@example.com", "Passw0rdX"));

        Assert.True(result.IsSuccess);
        Assert.Single(_tokens.AccessTokens);
        Assert.Single(_tokens.RefreshTokens);
    }

    [Fact]
    public async Task Login_fails_the_same_way_for_a_wrong_password_and_an_unknown_user()
    {
        var dispatcher = Dispatcher();
        SeedUser();

        var wrongPassword = await dispatcher.SendAsync(new LoginCommand("aof", "WrongPassw0rd"));
        var calls = _hasher.VerifyCalls;
        var unknownUser = await dispatcher.SendAsync(new LoginCommand("ghost", "Passw0rdX"));

        Assert.Equal(AuthenErrors.InvalidCredentials, wrongPassword.FirstError);
        Assert.Equal(AuthenErrors.InvalidCredentials, unknownUser.FirstError);
        Assert.Equal(calls + 1, _hasher.VerifyCalls); // the unknown user still cost one password check
        Assert.Empty(_tokens.AccessTokens);
    }

    [Fact]
    public async Task Login_refuses_an_inactive_account_after_the_password_check()
    {
        var dispatcher = Dispatcher();
        SeedUser();
        _users.OverrideStatus = UserStatus.Locked;

        var result = await dispatcher.SendAsync(new LoginCommand("aof", "Passw0rdX"));

        Assert.Equal(AuthenErrors.AccountNotActive, result.FirstError);
    }

    [Fact]
    public async Task Password_reset_request_sends_mail_only_for_known_emails_but_always_succeeds()
    {
        var dispatcher = Dispatcher();
        SeedUser();

        var known = await dispatcher.SendAsync(new RequestPasswordResetCommand("aof@example.com"));
        var unknown = await dispatcher.SendAsync(new RequestPasswordResetCommand("nobody@example.com"));

        Assert.True(known.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.Equal(["aof@example.com"], _email.Sent);
    }

    [Fact]
    public async Task Password_reset_replaces_the_password_consumes_the_token_and_revokes_sessions()
    {
        var dispatcher = Dispatcher();
        SeedUser();
        await dispatcher.SendAsync(new RequestPasswordResetCommand("aof@example.com"));

        var result = await dispatcher.SendAsync(new ResetPasswordCommand(_tokenService.LastResetToken, "NewPassw0rd"));

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
