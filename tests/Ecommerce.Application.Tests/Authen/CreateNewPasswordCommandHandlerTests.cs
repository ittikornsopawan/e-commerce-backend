using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.CreateNewPassword;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Tests.Authen;

// Orchestration of 04-create-new-password (database behavior in Ecommerce.Infrastructure.Tests, HTTP in Ecommerce.Api.Tests).
public sealed class CreateNewPasswordCommandHandlerTests
{
    private sealed class Repository(FakePasswordHasher hasher) : ICreateNewPasswordRepository
    {
        public ResetRoute? Route { get; set; } = new(Guid.NewGuid(), Guid.NewGuid());
        public bool UserActive { get; set; } = true;
        public bool Consumable { get; set; } = true;
        public CurrentPassword? Current { get; set; } = new(Guid.NewGuid(), hasher.Hash("Passw0rd-Example"));
        public List<string> Calls { get; } = [];

        public Task<ResetRoute?> FindResetAsync(byte[] h, DateTime n, CancellationToken ct) { Calls.Add("find"); return Task.FromResult(Route); }
        public Task<bool> LockActiveUserAsync(Guid u, CancellationToken ct) { Calls.Add("lock"); return Task.FromResult(UserActive); }
        public Task<bool> ConsumeResetAsync(ResetRoute r, byte[] h, DateTime n, CancellationToken ct) { Calls.Add("consume"); return Task.FromResult(Consumable); }
        public Task<CurrentPassword?> GetCurrentPasswordAsync(Guid u, CancellationToken ct) { Calls.Add("current"); return Task.FromResult(Current); }
        public Task<bool> ReplacePasswordAsync(Guid u, Guid p, PasswordCredential c, DateTime n, CancellationToken ct) { Calls.Add("replace"); return Task.FromResult(true); }
        public Task CloseAllTokensAsync(Guid u, DateTime n, CancellationToken ct) { Calls.Add("close"); return Task.CompletedTask; }
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

    private readonly FakePasswordHasher _hasher = new();
    private readonly Repository _repository;

    public CreateNewPasswordCommandHandlerTests() => _repository = new Repository(_hasher);

    private Task<Result> Create(string password = "NewSecret1x") =>
        new CreateNewPasswordCommandHandler(_repository, _hasher, new FakeTokenService(), new Runner(_repository.Calls), TimeProvider.System)
            .HandleAsync(new CreateNewPasswordCommand("raw-token", password), CancellationToken.None);

    [Fact]
    public async Task TC_NEWPW_001_follows_the_lock_order_and_commits()
    {
        Assert.True((await Create()).IsSuccess);
        Assert.Equal(["find", "begin ReadCommitted", "lock", "consume", "current", "replace", "close", "commit"], _repository.Calls);
    }

    [Fact]
    public async Task TC_NEWPW_004_an_unknown_token_is_400_without_a_transaction()
    {
        _repository.Route = null;
        Assert.Equal(AuthenErrors.InvalidResetToken, (await Create()).FirstError);
        Assert.Equal(["find"], _repository.Calls);
    }

    [Fact]
    public async Task TC_NEWPW_007_a_user_that_is_not_active_is_the_same_400_and_rolls_back()
    {
        _repository.UserActive = false;
        Assert.Equal(AuthenErrors.InvalidResetToken, (await Create()).FirstError);
        Assert.Equal("rollback", _repository.Calls[^1]);
        Assert.DoesNotContain("consume", _repository.Calls);
    }

    [Fact]
    public async Task TC_NEWPW_005_a_token_consumed_while_waiting_is_the_same_400()
    {
        _repository.Consumable = false;
        Assert.Equal(AuthenErrors.InvalidResetToken, (await Create()).FirstError);
        Assert.DoesNotContain("current", _repository.Calls);
        Assert.Equal("rollback", _repository.Calls[^1]);
    }

    [Fact]
    public async Task TC_NEWPW_008_the_same_password_is_400_and_rolls_the_consumption_back()
    {
        var error = (await Create("Passw0rd-Example")).FirstError;
        Assert.Equal(AuthenErrors.SamePassword, error);
        Assert.Equal("New password must be different from the current password.", error.Description);
        Assert.DoesNotContain("replace", _repository.Calls);
        Assert.Equal("rollback", _repository.Calls[^1]);
    }

    [Fact]
    public async Task TC_NEWPW_011_a_missing_current_password_is_an_unexpected_failure()
    {
        _repository.Current = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create());
    }
}
