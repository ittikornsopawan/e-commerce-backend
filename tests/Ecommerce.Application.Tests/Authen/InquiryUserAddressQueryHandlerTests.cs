using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Application.Features.Authen.InquiryUserAddress;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Tests.Authen;

// Spec 10 asks for one REPEATABLE READ READ ONLY snapshot (TC-ADDRESSQ-011); the HTTP tests prove the snapshot itself.
public sealed class InquiryUserAddressQueryHandlerTests
{
    private sealed class Credentials : ICallerCredentialRepository
    {
        public Task<bool> LockActiveUserAsync(Guid u, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsCurrentAccessTokenAsync(byte[] h, Guid u, DateTime n, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsCurrentCredentialAsync(byte[] h, Guid u, DateTime n, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class NoAddresses : IUserAddressQueryRepository
    {
        public Task<long> CountAsync(AddressFilter f, CancellationToken ct) => Task.FromResult(0L);
        public Task<IReadOnlyList<AddressRow>> ListAsync(AddressFilter f, long o, long l, CancellationToken ct) => throw new InvalidOperationException("no page when the count is 0");
    }

    private sealed class Runner : ITransactionRunner
    {
        public (IsolationLevel Isolation, bool ReadOnly)? Run { get; private set; }
        public Task<Result<T>> RunAsync<T>(IsolationLevel isolation, Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct, bool readOnly = false)
        {
            Run = (isolation, readOnly);
            return work(ct);
        }
    }

    [Fact]
    public async Task TC_ADDRESSQ_011_reads_in_one_repeatable_read_read_only_transaction()
    {
        var runner = new Runner();
        var credential = new CallerCredential(new Credentials(), new FakeTokenService(), TimeProvider.System);
        var caller = new Caller(Guid.NewGuid(), "jwt", DateTime.UtcNow.AddMinutes(5));

        var result = await new InquiryUserAddressQueryHandler(new NoAddresses(), credential, runner)
            .HandleAsync(new InquiryUserAddressQuery(caller, null, null, null, null, null), CancellationToken.None);

        Assert.Equal(0, result.Value.TotalItems);
        Assert.Equal((IsolationLevel.RepeatableRead, true), runner.Run);
    }
}
