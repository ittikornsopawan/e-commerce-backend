using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Authen.GetUserProfile;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Tests.Authen;

// Orchestration of 08-inquiry-user-profile.
public sealed class GetUserProfileQueryHandlerTests
{
    private sealed class Profiles : IUserProfileQueryRepository
    {
        public ProfileUser? User { get; set; } = new("john@example.test", "John", null, null, null);
        public List<string> Calls { get; } = [];
        public Task<ProfileUser?> GetUserAsync(Guid u, byte[] h, DateTime n, CancellationToken ct) { Calls.Add("user"); return Task.FromResult(User); }
        public Task<ProfileInformation?> GetInformationAsync(Guid u, CancellationToken ct) { Calls.Add("information"); return Task.FromResult<ProfileInformation?>(null); }
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

    private readonly Profiles _profiles = new();
    private readonly Runner _runner = new();
    private static readonly Caller Caller = new(Guid.NewGuid(), "jwt", DateTime.UtcNow.AddMinutes(5));

    private Task<Result<UserProfileResponse>> Get(Caller? caller) =>
        new GetUserProfileQueryHandler(_profiles, new FakeTokenService(), _runner, TimeProvider.System).HandleAsync(new GetUserProfileQuery(caller), CancellationToken.None);

    [Fact]
    public async Task TC_PROFILEQ_005_reads_the_user_then_the_information_in_one_repeatable_read_snapshot()
    {
        var result = await Get(Caller);
        Assert.Equal(Caller.UserId, result.Value.UserId);
        Assert.Null(result.Value.PhoneNumber);
        Assert.Equal(IsolationLevel.RepeatableRead, _runner.Isolation);
        Assert.Equal(["user", "information"], _profiles.Calls);
    }

    [Fact]
    public async Task TC_PROFILEQ_004_no_caller_an_expired_jwt_or_no_joined_row_is_401_and_skips_the_information()
    {
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Get(null)).FirstError);
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Get(Caller with { TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1) })).FirstError);
        _profiles.User = null;
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Get(Caller)).FirstError);
        Assert.DoesNotContain("information", _profiles.Calls);
    }
}
