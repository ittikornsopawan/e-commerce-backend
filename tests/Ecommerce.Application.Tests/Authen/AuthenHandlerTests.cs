using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Application.Features.Authen.Login;
using Ecommerce.Application.Features.Authen.Register;
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

    private static RegisterCommand ValidRegister(string username = "Aof", string email = "Aof@Example.com") =>
        new(username, email, "Passw0rdX", "Ittikorn", "S", null);

    [Fact]
    public async Task Register_stores_normalized_user_and_password()
    {
        var result = await Dispatcher().SendAsync(ValidRegister());

        Assert.True(result.IsSuccess);
        var user = Assert.Single(_users.Users);
        Assert.Equal("aof", user.Username.Value);
        Assert.Equal("aof@example.com", user.Email.Value);
        Assert.Equal("Ittikorn S", result.Value.DisplayName);
        Assert.True(_passwords.Active.ContainsKey(user.Id));
    }

    [Fact]
    public async Task Register_rejects_a_taken_username_and_a_taken_email()
    {
        var dispatcher = Dispatcher();
        await dispatcher.SendAsync(ValidRegister());

        var sameUsername = await dispatcher.SendAsync(ValidRegister("AOF", "other@example.com"));
        var sameEmail = await dispatcher.SendAsync(ValidRegister("other", "aof@example.com"));

        Assert.Equal(AuthenErrors.UsernameTaken, sameUsername.FirstError);
        Assert.Equal(AuthenErrors.EmailTaken, sameEmail.FirstError);
        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task Register_turns_a_concurrent_duplicate_into_a_conflict()
    {
        _users.ThrowDuplicateOnAdd = true;

        var result = await Dispatcher().SendAsync(ValidRegister());

        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
    }

    [Fact]
    public async Task Invalid_input_is_rejected_before_the_handler_runs()
    {
        var result = await Dispatcher().SendAsync(new RegisterCommand("a", "bad", "short", "", "", null));

        Assert.True(result.IsFailure);
        Assert.All(result.Errors, error => Assert.Equal(ErrorType.Validation, error.Type));
        Assert.Empty(_users.Users);
        Assert.Equal(0, _hasher.VerifyCalls);
    }

    [Fact]
    public async Task Login_issues_and_stores_tokens_for_the_right_password()
    {
        var dispatcher = Dispatcher();
        await dispatcher.SendAsync(ValidRegister());

        var result = await dispatcher.SendAsync(new LoginCommand("aof@example.com", "Passw0rdX"));

        Assert.True(result.IsSuccess);
        Assert.Single(_tokens.AccessTokens);
        Assert.Single(_tokens.RefreshTokens);
    }

    [Fact]
    public async Task Login_fails_the_same_way_for_a_wrong_password_and_an_unknown_user()
    {
        var dispatcher = Dispatcher();
        await dispatcher.SendAsync(ValidRegister());

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
        await dispatcher.SendAsync(ValidRegister());
        _users.OverrideStatus = UserStatus.Locked;

        var result = await dispatcher.SendAsync(new LoginCommand("aof", "Passw0rdX"));

        Assert.Equal(AuthenErrors.AccountNotActive, result.FirstError);
    }

    [Fact]
    public async Task Password_reset_request_sends_mail_only_for_known_emails_but_always_succeeds()
    {
        var dispatcher = Dispatcher();
        await dispatcher.SendAsync(ValidRegister());

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
        await dispatcher.SendAsync(ValidRegister());
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
