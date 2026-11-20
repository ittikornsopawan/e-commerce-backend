using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.Logout;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Tests.Authen;

// Orchestration of 06-logout (database behavior in Ecommerce.Infrastructure.Tests, HTTP in Ecommerce.Api.Tests).
public sealed class LogoutCommandHandlerTests
{
    private sealed class Repository : ILogoutRepository
    {
        public bool UserActive { get; set; } = true;
        public Guid? Session { get; set; } = Guid.NewGuid();
        public int Closed { get; set; } = 1;
        public List<string> Calls { get; } = [];
        public Task<bool> LockActiveUserAsync(Guid u, CancellationToken ct) { Calls.Add("lock"); return Task.FromResult(UserActive); }
        public Task<Guid?> GetSessionAsync(byte[] h, Guid u, DateTime n, CancellationToken ct) { Calls.Add("session"); return Task.FromResult(Session); }
        public Task<int> CloseSessionAsync(Guid s, Guid u, DateTime n, CancellationToken ct) { Calls.Add("close"); return Task.FromResult(Closed); }
    }

    private sealed class Runner(List<string> calls) : ITransactionRunner
    {
        public async Task<Result<T>> RunAsync<T>(IsolationLevel isolation, Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct, bool readOnly = false)
        {
            calls.Add($"begin {isolation}");
            var result = await work(ct);
            calls.Add(result.IsSuccess ? "commit" : "rollback");
            return result;
        }
    }

    private readonly Repository _repository = new();

    private Task<Result> Logout(int expiresInMinutes = 10) =>
        new LogoutCommandHandler(_repository, new FakeTokenService(), new Runner(_repository.Calls), TimeProvider.System)
            .HandleAsync(new LogoutCommand(Guid.NewGuid(), "jwt", DateTime.UtcNow.AddMinutes(expiresInMinutes)), CancellationToken.None);

    [Fact]
    public async Task TC_LOGOUT_001_locks_then_reads_the_session_then_closes_and_commits()
    {
        Assert.True((await Logout()).IsSuccess);
        Assert.Equal(["begin ReadCommitted", "lock", "session", "close", "commit"], _repository.Calls);
    }

    [Fact]
    public async Task TC_LOGOUT_005_a_user_that_is_not_active_is_401_and_rolls_back()
    {
        _repository.UserActive = false;
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Logout()).FirstError);
        Assert.Equal(["begin ReadCommitted", "lock", "rollback"], _repository.Calls);
    }

    [Fact]
    public async Task TC_LOGOUT_004_a_missing_stored_token_is_401_without_writes()
    {
        _repository.Session = null;
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Logout()).FirstError);
        Assert.DoesNotContain("close", _repository.Calls);
    }

    [Fact]
    public async Task TC_LOGOUT_004_a_jwt_that_expired_while_waiting_is_401()
    {
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Logout(expiresInMinutes: -1)).FirstError);
        Assert.DoesNotContain("session", _repository.Calls);
    }

    [Fact]
    public async Task TC_LOGOUT_008_zero_closed_access_rows_is_an_unexpected_failure()
    {
        _repository.Closed = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Logout());
    }
}
