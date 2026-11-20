using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Authen.UpsertUserProfile;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Tests.Authen;

// Orchestration of 07-upsert-user-profile (database behavior in Ecommerce.Infrastructure.Tests, HTTP in Ecommerce.Api.Tests).
public sealed class UpsertUserProfileCommandHandlerTests
{
    private sealed class Credentials : ICallerCredentialRepository
    {
        public bool UserActive { get; set; } = true;
        public bool TokenCurrent { get; set; } = true;
        public Task<bool> LockActiveUserAsync(Guid u, CancellationToken ct) => Task.FromResult(UserActive);
        public Task<bool> IsCurrentAccessTokenAsync(byte[] h, Guid u, DateTime n, CancellationToken ct) => Task.FromResult(TokenCurrent);
        public Task<bool> IsCurrentCredentialAsync(byte[] h, Guid u, DateTime n, CancellationToken ct) => Task.FromResult(UserActive && TokenCurrent);
    }

    private sealed class Profiles : IUserProfileRepository
    {
        public Guid? InformationId { get; set; }
        public List<string> Calls { get; } = [];
        public Task<bool> UpdateUserAsync(Guid u, ProfileUserChanges c, DateTime n, CancellationToken ct) { Calls.Add("user"); return Task.FromResult(true); }
        public Task<Guid?> FindInformationIdAsync(Guid u, CancellationToken ct) { Calls.Add("find"); return Task.FromResult(InformationId); }
        public Task<Guid> InsertInformationAsync(Guid u, ProfileInformationChanges c, CancellationToken ct) { Calls.Add("insert"); return Task.FromResult(Guid.NewGuid()); }
        public Task<bool> UpdateInformationAsync(Guid i, Guid u, ProfileInformationChanges c, DateTime n, CancellationToken ct) { Calls.Add("update"); return Task.FromResult(true); }
    }

    private sealed class Runner : ITransactionRunner
    {
        public Task<Result<T>> RunAsync<T>(IsolationLevel isolation, Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct, bool readOnly = false) => work(ct);
    }

    private readonly Credentials _credentials = new();
    private readonly Profiles _profiles = new();
    private static readonly Caller Caller = new(Guid.NewGuid(), "jwt", DateTime.UtcNow.AddMinutes(10));

    private Task<Result<UpsertUserProfileResponse>> Upsert(Caller? caller, Optional<string?> firstName = default, Optional<string?> gender = default) =>
        new UpsertUserProfileCommandHandler(new CallerCredential(_credentials, new FakeTokenService(), TimeProvider.System), _profiles, new Runner())
            .HandleAsync(new UpsertUserProfileCommand(caller, firstName, default, default, default, default, default, default, gender), CancellationToken.None);

    [Fact]
    public async Task TC_PROFILE_004_an_empty_body_writes_nothing_and_returns_the_user_id()
    {
        var result = await Upsert(Caller);
        Assert.Equal(Caller.UserId, result.Value.UserId);
        Assert.Empty(_profiles.Calls);
    }

    [Fact]
    public async Task TC_PROFILE_005_a_user_field_only_skips_the_information_lookup()
    {
        await Upsert(Caller, firstName: Optional<string?>.Of(null));
        Assert.Equal(["user"], _profiles.Calls);
    }

    [Fact]
    public async Task TC_PROFILE_002_an_information_field_inserts_when_there_is_no_row_and_updates_otherwise()
    {
        await Upsert(Caller, gender: Optional<string?>.Of("OTHER"));
        _profiles.InformationId = Guid.NewGuid();
        await Upsert(Caller, gender: Optional<string?>.Of(null));
        Assert.Equal(["find", "insert", "find", "update"], _profiles.Calls);
    }

    [Fact]
    public async Task TC_PROFILE_010_every_credential_failure_is_the_same_401_without_writes()
    {
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Upsert(null, firstName: Optional<string?>.Of("x"))).FirstError);
        _credentials.TokenCurrent = false;
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Upsert(Caller, firstName: Optional<string?>.Of("x"))).FirstError);
        _credentials.UserActive = false;
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Upsert(Caller, firstName: Optional<string?>.Of("x"))).FirstError);
        Assert.Equal(AuthenErrors.AccessTokenInvalid, (await Upsert(Caller with { TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1) })).FirstError);
        Assert.Empty(_profiles.Calls);
    }
}
