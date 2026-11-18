using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Authen.Login;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Tests.Authen;

// Orchestration of 02-login (the database behavior is in Ecommerce.Infrastructure.Tests, HTTP in Ecommerce.Api.Tests).
public sealed class LoginCommandHandlerTests
{
    private sealed class Repository : ILoginRepository
    {
        public List<string> Calls { get; } = [];
        public int Failures { get; set; }
        public List<LoginUser> Users { get; } = [];
        public PasswordCredential? Password { get; set; }
        public List<(Guid? UserId, LoginResult Result)> Attempts { get; } = [];
        public LoginSession? Session { get; private set; }
        public Exception? ThrowOnTokens { get; set; }

        public Task LockIdentifierAsync(string identifier, CancellationToken ct) { Calls.Add("lock-identifier"); return Task.CompletedTask; }
        public Task<int> CountRecentFailuresAsync(string identifier, DateTime windowStart, CancellationToken ct) { Calls.Add("count"); return Task.FromResult(Failures); }
        public Task<IReadOnlyList<LoginUser>> LockActiveUsersAsync(string identifier, CancellationToken ct) { Calls.Add("lock-user"); return Task.FromResult<IReadOnlyList<LoginUser>>(Users); }
        public Task<PasswordCredential?> GetCurrentPasswordAsync(Guid userId, CancellationToken ct) { Calls.Add("password"); return Task.FromResult(Password); }
        public Task<AuthorizationFacts> GetAuthorizationFactsAsync(Guid userId, CancellationToken ct) { Calls.Add("facts"); return Task.FromResult(new AuthorizationFacts([], [], [], [], [], [])); }
        public Task<IReadOnlyList<ScopeFact>> GetActiveScopesAsync(IReadOnlyList<Guid> ids, CancellationToken ct) { Calls.Add("scopes"); return Task.FromResult<IReadOnlyList<ScopeFact>>([]); }
        public Task AddSessionTokensAsync(LoginSession session, CancellationToken ct)
        {
            Calls.Add("tokens");
            if (ThrowOnTokens is not null) throw ThrowOnTokens;
            Session = session;
            return Task.CompletedTask;
        }
        public Task RecordAttemptAsync(string identifier, Guid? userId, LoginResult result, DateTime now, CancellationToken ct)
        {
            Calls.Add("attempt");
            Attempts.Add((userId, result));
            return Task.CompletedTask;
        }
    }

    private sealed class Runner : ITransactionRunner
    {
        public IsolationLevel? Isolation { get; private set; }
        public Task<Result<T>> RunAsync<T>(IsolationLevel isolation, Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct)
        {
            Isolation = isolation;
            return work(ct);
        }
    }

    private readonly Repository _repository = new();
    private readonly Runner _runner = new();
    private readonly FakePasswordHasher _hasher = new();
    private static readonly LoginUser User = new(Guid.NewGuid(), "john", "john@example.test", null);

    private Task<Result<TokenPairResponse>> Login(string password = "Passw0rd-Example") =>
        new LoginCommandHandler(_repository, _hasher, new FakeTokenService(), _runner, new LoginSettings(TimeSpan.FromMinutes(15)), TimeProvider.System)
            .HandleAsync(new LoginCommand("john", password), CancellationToken.None);

    private void KnownUser()
    {
        _repository.Users.Add(User);
        _repository.Password = _hasher.Hash("Passw0rd-Example");
    }

    [Fact]
    public async Task TC_LOGIN_001_success_follows_the_lock_order_and_records_SUCCESS()
    {
        KnownUser();

        var result = await Login();

        Assert.True(result.IsSuccess);
        Assert.Equal(("Bearer", 900), (result.Value.TokenType, result.Value.ExpiresIn));
        Assert.Equal(IsolationLevel.ReadCommitted, _runner.Isolation);
        Assert.Equal(["lock-identifier", "count", "lock-user", "password", "facts", "scopes", "tokens", "attempt"], _repository.Calls);
        Assert.Equal([(User.Id, LoginResult.Success)], _repository.Attempts);
        Assert.Equal(User.Id, _repository.Session!.UserId);
    }

    [Fact]
    public async Task TC_LOGIN_013_five_failures_refuse_without_user_lookup_or_password_check()
    {
        KnownUser();
        _repository.Failures = 5;

        var result = await Login();

        Assert.Equal(AuthenErrors.InvalidCredentials, result.FirstError);
        Assert.Equal(["lock-identifier", "count", "attempt"], _repository.Calls);
        Assert.Equal([((Guid?)null, LoginResult.Refused)], _repository.Attempts);
        Assert.Equal(0, _hasher.VerifyCalls);
    }

    [Fact]
    public async Task TC_LOGIN_005_TC_LOGIN_010_unknown_or_ambiguous_does_a_dummy_check_and_records_FAILED_without_user()
    {
        Assert.Equal(AuthenErrors.InvalidCredentials, (await Login()).FirstError);
        _repository.Users.AddRange([User, User with { Id = Guid.NewGuid() }]);
        Assert.Equal(AuthenErrors.InvalidCredentials, (await Login()).FirstError);

        Assert.Equal(2, _hasher.VerifyCalls);
        Assert.Equal([((Guid?)null, LoginResult.Failed), (null, LoginResult.Failed)], _repository.Attempts);
        Assert.DoesNotContain("password", _repository.Calls);
    }

    [Fact]
    public async Task TC_LOGIN_006_TC_LOGIN_009_wrong_or_missing_password_records_FAILED_with_the_user()
    {
        KnownUser();
        Assert.Equal(AuthenErrors.InvalidCredentials, (await Login("Wrong-Passw0rd")).FirstError);
        _repository.Password = null;
        Assert.Equal(AuthenErrors.InvalidCredentials, (await Login()).FirstError);

        Assert.Equal(2, _hasher.VerifyCalls);
        Assert.Equal([((Guid?)User.Id, LoginResult.Failed), (User.Id, LoginResult.Failed)], _repository.Attempts);
        Assert.DoesNotContain("tokens", _repository.Calls);
    }

    [Fact]
    public async Task TC_LOGIN_019_a_timeout_is_408()
    {
        KnownUser();
        _repository.ThrowOnTokens = new TimeoutException();
        var error = (await Login()).FirstError;
        Assert.Equal(("E90001", ErrorType.Timeout), (error.BizCode, error.Type));
    }

    [Fact]
    public async Task TC_LOGIN_020_an_unexpected_failure_is_not_swallowed()
    {
        KnownUser();
        _repository.ThrowOnTokens = new InvalidOperationException("boom");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Login());
    }
}
