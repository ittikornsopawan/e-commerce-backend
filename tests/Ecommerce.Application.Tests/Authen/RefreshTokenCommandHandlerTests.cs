using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.RefreshToken;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Tests.Authen;

// Orchestration of 05-refresh-token (database behavior in Ecommerce.Infrastructure.Tests, HTTP in Ecommerce.Api.Tests).
public sealed class RefreshTokenCommandHandlerTests
{
    private static readonly LoginUser User = new(Guid.NewGuid(), "john", "john@example.test", null);
    private static readonly Guid Session = Guid.NewGuid();

    private sealed class Refreshes : IRefreshTokenRepository
    {
        public RefreshOwner? Owner { get; set; } = new(Guid.NewGuid(), User.Id);
        public LoginUser? LockedUser { get; set; } = User;
        public RefreshTokenRow? Row { get; set; }
        public bool Consumable { get; set; } = true;
        public List<string> Calls { get; } = [];

        public Task<RefreshOwner?> FindOwnerAsync(byte[] h, DateTime n, CancellationToken ct) { Calls.Add("find"); return Task.FromResult(Owner); }
        public Task<LoginUser?> LockActiveUserAsync(Guid u, CancellationToken ct) { Calls.Add("lock"); return Task.FromResult(LockedUser); }
        public Task<RefreshTokenRow?> RereadAsync(RefreshOwner o, byte[] h, CancellationToken ct) { Calls.Add("reread"); return Task.FromResult(Row); }
        public Task<bool> ConsumeAsync(RefreshTokenRow r, byte[] h, DateTime n, CancellationToken ct) { Calls.Add("consume"); return Task.FromResult(Consumable); }
        public Task CloseSessionAsync(Guid s, Guid u, DateTime n, CancellationToken ct) { Calls.Add($"close {s == Session}"); return Task.CompletedTask; }
    }

    private sealed class Logins(List<string> calls) : ILoginRepository
    {
        public LoginSession? Session { get; private set; }
        public Task LockIdentifierAsync(string i, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountRecentFailuresAsync(string i, DateTime w, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LoginUser>> LockActiveUsersAsync(string i, CancellationToken ct) => throw new NotSupportedException();
        public Task<PasswordCredential?> GetCurrentPasswordAsync(Guid u, CancellationToken ct) => throw new NotSupportedException();
        public Task<AuthorizationFacts> GetAuthorizationFactsAsync(Guid u, CancellationToken ct) { calls.Add("facts"); return Task.FromResult(new AuthorizationFacts([], [], [], [], [], [])); }
        public Task<IReadOnlyList<ScopeFact>> GetActiveScopesAsync(IReadOnlyList<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<ScopeFact>>([]);
        public Task AddSessionTokensAsync(LoginSession s, CancellationToken ct) { calls.Add("tokens"); Session = s; return Task.CompletedTask; }
        public Task RecordAttemptAsync(string i, Guid? u, LoginResult r, DateTime n, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Runner(List<string> calls) : ITransactionRunner
    {
        public async Task<Result<T>> RunAsync<T>(IsolationLevel isolation, Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct)
        {
            calls.Add($"begin {isolation}");
            var result = await work(ct);
            calls.Add(result.IsSuccess ? "commit" : "rollback");
            return result;
        }
    }

    private readonly Refreshes _refreshes = new();
    private readonly Logins _logins;

    public RefreshTokenCommandHandlerTests() => _logins = new Logins(_refreshes.Calls);

    private static RefreshTokenRow Row(string status, DateTime? updatedAt = null) =>
        new(Guid.NewGuid(), User.Id, Session, status, updatedAt, DateTime.UtcNow.AddDays(1));

    private Task<Result<Ecommerce.Application.Features.Authen.Common.TokenPairResponse>> Refresh() =>
        new RefreshTokenCommandHandler(_refreshes, _logins, new FakeTokenService(), new Runner(_refreshes.Calls),
                new RefreshSettings(TimeSpan.FromSeconds(10)), TimeProvider.System)
            .HandleAsync(new RefreshTokenCommand("raw"), CancellationToken.None);

    [Fact]
    public async Task TC_REFRESH_001_rotation_consumes_then_issues_in_the_same_session()
    {
        _refreshes.Row = Row("ACTIVE");

        var result = await Refresh();

        Assert.True(result.IsSuccess);
        Assert.Equal(("Bearer", 900), (result.Value.TokenType, result.Value.ExpiresIn));
        Assert.Equal(["find", "begin ReadCommitted", "lock", "reread", "consume", "facts", "tokens", "commit"], _refreshes.Calls);
        Assert.Equal(Session, _logins.Session!.SessionId);
    }

    [Fact]
    public async Task TC_REFRESH_004_an_unknown_token_is_401_without_a_transaction()
    {
        _refreshes.Owner = null;
        Assert.Equal(AuthenErrors.RefreshTokenInvalid, (await Refresh()).FirstError);
        Assert.Equal(["find"], _refreshes.Calls);
    }

    [Fact]
    public async Task TC_REFRESH_005_a_user_that_is_not_active_is_401_and_rolls_back()
    {
        _refreshes.LockedUser = null;
        Assert.Equal(AuthenErrors.RefreshTokenInvalid, (await Refresh()).FirstError);
        Assert.Equal("rollback", _refreshes.Calls[^1]);
    }

    [Fact]
    public async Task TC_REFRESH_006_a_recent_replay_is_401_and_rolls_back_without_closing()
    {
        _refreshes.Row = Row("INACTIVE", DateTime.UtcNow.AddSeconds(-1));
        Assert.Equal(AuthenErrors.RefreshTokenInvalid, (await Refresh()).FirstError);
        Assert.DoesNotContain(_refreshes.Calls, c => c.StartsWith("close"));
        Assert.Equal("rollback", _refreshes.Calls[^1]);
    }

    [Fact]
    public async Task TC_REFRESH_007_an_old_replay_closes_the_session_commits_and_answers_401()
    {
        _refreshes.Row = Row("INACTIVE", DateTime.UtcNow.AddSeconds(-30));
        Assert.Equal(AuthenErrors.RefreshTokenInvalid, (await Refresh()).FirstError);
        Assert.Equal(["find", "begin ReadCommitted", "lock", "reread", "close True", "commit"], _refreshes.Calls);
    }

    [Fact]
    public async Task TC_REFRESH_008_a_failed_consume_is_401_and_issues_nothing()
    {
        _refreshes.Row = Row("ACTIVE");
        _refreshes.Consumable = false;
        Assert.Equal(AuthenErrors.RefreshTokenInvalid, (await Refresh()).FirstError);
        Assert.DoesNotContain("tokens", _refreshes.Calls);
    }
}
