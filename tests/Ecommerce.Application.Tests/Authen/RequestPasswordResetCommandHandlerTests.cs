using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.ResetPassword;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Tests.Authen;

// Orchestration of 03-reset-password (database behavior in Ecommerce.Infrastructure.Tests, HTTP in Ecommerce.Api.Tests).
public sealed class RequestPasswordResetCommandHandlerTests
{
    private sealed class Repository : IPasswordResetRequestRepository
    {
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public bool StillEligible { get; set; } = true;
        public int Requests { get; set; }
        public Exception? ThrowOnReplace { get; set; }
        public List<string> Calls { get; } = [];
        public (byte[] Hash, DateTime Now, DateTime Expires)? Replaced { get; private set; }

        public Task<Guid?> FindActiveUserIdAsync(string e, CancellationToken ct) { Calls.Add("find"); return Task.FromResult(UserId); }
        public Task<bool> LockActiveUserAsync(Guid u, string e, CancellationToken ct) { Calls.Add("lock"); return Task.FromResult(StillEligible); }
        public Task<int> CountRequestsAsync(Guid u, DateTime w, CancellationToken ct) { Calls.Add("count"); return Task.FromResult(Requests); }
        public Task<Guid> ReplaceActiveResetAsync(Guid u, byte[] h, DateTime n, DateTime x, CancellationToken ct)
        {
            Calls.Add("replace");
            if (ThrowOnReplace is not null) throw ThrowOnReplace;
            Replaced = (h, n, x);
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class Runner(List<string> calls) : ITransactionRunner
    {
        public async Task<Result<T>> RunAsync<T>(IsolationLevel isolation, Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct)
        {
            calls.Add($"begin {isolation}");
            var result = await work(ct);
            calls.Add("commit");
            return result;
        }
    }

    private readonly Repository _repository = new();
    private readonly FakeEmailSender _email = new();
    private readonly FakeTokenService _tokens = new();

    private Task<Result> Request() =>
        new RequestPasswordResetCommandHandler(_repository, _tokens, _email, new Runner(_repository.Calls),
                new PasswordResetSettings(3, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30), "https://reset.test/?token="), TimeProvider.System)
            .HandleAsync(new RequestPasswordResetCommand("john@example.test"), CancellationToken.None);

    [Fact]
    public async Task TC_RESET_001_issues_in_order_and_hands_off_the_link_after_commit()
    {
        Assert.True((await Request()).IsSuccess);

        Assert.Equal(["find", "begin ReadCommitted", "lock", "count", "replace", "commit"], _repository.Calls);
        var (to, link) = Assert.Single(_email.Sent);
        Assert.Equal("john@example.test", to);
        Assert.Equal("https://reset.test/?token=" + _tokens.LastResetToken, link);
        Assert.Equal(_tokens.Hash(_tokens.LastResetToken), _repository.Replaced!.Value.Hash);
        Assert.Equal(TimeSpan.FromMinutes(30), _repository.Replaced.Value.Expires - _repository.Replaced.Value.Now);
    }

    [Fact]
    public async Task TC_RESET_004_an_unknown_email_succeeds_without_a_transaction_or_email_but_after_the_same_preparation()
    {
        _repository.UserId = null;
        Assert.True((await Request()).IsSuccess);
        Assert.Equal(["find"], _repository.Calls);
        Assert.Empty(_email.Sent);
        Assert.NotEmpty(_tokens.LastResetToken);
    }

    [Fact]
    public async Task TC_RESET_005_a_user_no_longer_eligible_at_the_lock_succeeds_without_writes()
    {
        _repository.StillEligible = false;
        Assert.True((await Request()).IsSuccess);
        Assert.DoesNotContain("count", _repository.Calls);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task TC_RESET_006_three_requests_in_the_window_refuse_without_writes_or_email()
    {
        _repository.Requests = 3;
        Assert.True((await Request()).IsSuccess);
        Assert.DoesNotContain("replace", _repository.Calls);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task TC_RESET_009_a_timeout_is_408_and_sends_nothing()
    {
        _repository.ThrowOnReplace = new TimeoutException();
        var error = (await Request()).FirstError;
        Assert.Equal("E90001", error.BizCode);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task TC_RESET_010_an_unexpected_failure_is_not_swallowed_and_sends_nothing()
    {
        _repository.ThrowOnReplace = new InvalidOperationException("boom");
        await Assert.ThrowsAsync<InvalidOperationException>(Request);
        Assert.Empty(_email.Sent);
    }
}
