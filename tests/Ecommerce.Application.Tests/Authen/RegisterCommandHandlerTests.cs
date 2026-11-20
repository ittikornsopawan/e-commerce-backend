using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.Register;
using Ecommerce.Domain;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Tests.Authen;

// Orchestration of 01-register: how the handler answers each outcome of the transaction (the real database
// behavior is proven in Ecommerce.Infrastructure.Tests, the HTTP answers in Ecommerce.Api.Tests).
public sealed class RegisterCommandHandlerTests
{
    private sealed class Repository : IRegistrationRepository
    {
        public bool Exists { get; set; }
        public Exception? ThrowOnAdd { get; set; }
        public NewRegistration? Added { get; private set; }
        public Task<bool> IdentifierExistsAsync(string u, string e, CancellationToken ct) => Task.FromResult(Exists);
        public Task<IReadOnlyList<ApplicableConsent>> GetApplicableConsentsAsync(DateTime now, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ApplicableConsent>>([]);
        public Task<bool> UserExistsAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<Guid> AddAsync(NewRegistration r, CancellationToken ct)
        {
            if (ThrowOnAdd is not null) throw ThrowOnAdd;
            Added = r;
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class Runner : ITransactionRunner
    {
        public IsolationLevel? Isolation { get; private set; }
        public Task<Result<T>> RunAsync<T>(IsolationLevel isolation, Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct, bool readOnly = false)
        {
            Isolation = isolation;
            return work(ct);
        }
    }

    private readonly Repository _repository = new();
    private readonly Runner _runner = new();

    private Task<Result<RegisterResponse>> Handle() =>
        new RegisterCommandHandler(_repository, new FakePasswordHasher(), _runner, TimeProvider.System).HandleAsync(
            new RegisterCommand("user", "user@example.test", "Passw0rd", null, null, null, null, null, null, "1990-01-31", "MALE", null, null, null),
            CancellationToken.None);

    [Fact]
    public async Task TC_REG_020_runs_in_REPEATABLE_READ_and_returns_the_new_user_id()
    {
        var result = await Handle();

        Assert.True(result.IsSuccess);
        Assert.Equal(IsolationLevel.RepeatableRead, _runner.Isolation);
        Assert.Equal(new DateOnly(1990, 1, 31), _repository.Added!.DateOfBirth);
    }

    [Fact]
    public async Task TC_REG_011_an_existing_identifier_is_409_and_nothing_is_added()
    {
        _repository.Exists = true;

        Assert.Equal(AuthenErrors.IdentifierTaken, (await Handle()).FirstError);
        Assert.Null(_repository.Added);
    }

    [Fact]
    public async Task TC_REG_012_a_concurrent_duplicate_is_the_same_409()
    {
        _repository.ThrowOnAdd = new DuplicateKeyException("m_user_username_key", "authen.m_user");
        Assert.Equal(AuthenErrors.IdentifierTaken, (await Handle()).FirstError);
    }

    [Fact]
    public async Task TC_REG_023_a_reference_deleted_concurrently_is_404_E10005()
    {
        _repository.ThrowOnAdd = new MissingReferenceException("l_user_referral_referrer_id_fkey");
        Assert.Equal(AuthenErrors.ConsentOrReferrerNotFound, (await Handle()).FirstError);
    }

    [Fact]
    public async Task TC_REG_024_a_timeout_is_408_E90001()
    {
        _repository.ThrowOnAdd = new TimeoutException();
        var error = (await Handle()).FirstError;
        Assert.Equal(ErrorType.Timeout, error.Type);
        Assert.Equal("E90001", error.BizCode);
        Assert.Equal("The server timed out waiting for the request.", error.Description);
    }

    [Fact]
    public async Task TC_REG_022_an_unexpected_failure_is_not_swallowed()
    {
        _repository.ThrowOnAdd = new InvalidOperationException("boom");
        await Assert.ThrowsAsync<InvalidOperationException>(Handle);
    }

    [Fact]
    public async Task TC_REG_022_a_unique_violation_on_a_child_table_is_not_a_409()
    {
        _repository.ThrowOnAdd = new DuplicateKeyException("m_user_information_pkey", "authen.m_user_information");
        await Assert.ThrowsAsync<DuplicateKeyException>(Handle);
    }
}
