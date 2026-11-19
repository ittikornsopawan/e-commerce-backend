using System.Data;
using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Common;
using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Domain;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.UpsertUserAddress;

/// <summary>
/// Orchestrates spec 09 in one READ COMMITTED transaction: current credential (user lock) → owned target → insert quota
/// → other ACTIVE address → resolved default → clear the old default → insert or update/delete → promotion.
/// A failure result rolls everything back, so an old default is never lost.
/// </summary>
public sealed class UpsertUserAddressCommandHandler(
    CallerCredential credential,
    IUserAddressRepository addresses,
    ITransactionRunner transactions,
    AddressSettings settings) : ICommandHandler<UpsertUserAddressCommand, UpsertUserAddressResult>
{
    private sealed record Target(Guid? Id, Guid UserId, string RowStatus, AddressValues Values, bool RequestedTrue);

    public async Task<Result<UpsertUserAddressResult>> HandleAsync(UpsertUserAddressCommand r, CancellationToken cancellationToken)
    {
        try
        {
            return await transactions.RunAsync(IsolationLevel.ReadCommitted, ct => RunAsync(r, ct), cancellationToken);
        }
        catch (TimeoutException)
        {
            return CommonErrors.Timeout;
        }
    }

    private async Task<Result<UpsertUserAddressResult>> RunAsync(UpsertUserAddressCommand r, CancellationToken ct)
    {
        var verified = await credential.VerifyAsync(r.Caller, ct);
        if (verified.IsFailure) return Result.Failure<UpsertUserAddressResult>(verified.FirstError);

        // The validator has checked every value.
        var target = new Target(
            r.Id.IsSet ? Guid.Parse(r.Id.Value!) : null, r.Caller!.UserId, r.RowStatus.IsSet ? r.RowStatus.Value! : AddressDefaultPolicy.Active,
            new AddressValues(r.Title!, r.Address!, r.City!, r.State!, r.PostalCode!, r.Country!), r.IsDefault == true);

        return target.Id is null ? await InsertAsync(target, verified.Value, ct) : await UpdateAsync(target, verified.Value, ct);
    }

    private async Task<Result<UpsertUserAddressResult>> InsertAsync(Target t, DateTime now, CancellationToken ct)
    {
        if (AddressQuota.IsFull(await addresses.CountAsync(t.UserId, ct), settings.MaxAddresses))
            return Result.Failure<UpsertUserAddressResult>(AuthenErrors.AddressLimit(settings.MaxAddresses));

        var decision = await DecideAsync(t, wasDefault: false, ct);
        if (decision.IsDefault) await addresses.ClearDefaultAsync(t.UserId, null, now, ct);

        var id = await addresses.InsertAsync(t.UserId, t.Values, t.RowStatus, decision.IsDefault, now, ct);
        return Result.Success(new UpsertUserAddressResult(id, AddressAction.Insert));
    }

    private async Task<Result<UpsertUserAddressResult>> UpdateAsync(Target t, DateTime now, CancellationToken ct)
    {
        var id = t.Id!.Value;
        var owned = await addresses.FindOwnedAsync(id, t.UserId, ct);
        if (owned is null) return Result.Failure<UpsertUserAddressResult>(AuthenErrors.AddressNotFound);

        var decision = await DecideAsync(t, owned.IsDefault, ct);
        if (decision.IsDefault) await addresses.ClearDefaultAsync(t.UserId, id, now, ct);
        if (!await addresses.UpdateAsync(id, t.UserId, t.Values, t.RowStatus, decision.IsDefault, now, ct))
            return Result.Failure<UpsertUserAddressResult>(AuthenErrors.AddressNotFound);

        if (decision.ShouldPromote) await addresses.PromoteDefaultAsync(t.UserId, now, ct);
        var action = t.RowStatus == "DELETED" ? AddressAction.Delete : AddressAction.Update;
        return Result.Success(new UpsertUserAddressResult(id, action));
    }

    private async Task<AddressDefaultDecision> DecideAsync(Target t, bool wasDefault, CancellationToken ct)
    {
        // Only a target that will be ACTIVE needs to know whether another ACTIVE address exists.
        var otherActive = t.RowStatus == AddressDefaultPolicy.Active && await addresses.OtherActiveExistsAsync(t.UserId, t.Id, ct);
        return AddressDefaultPolicy.Resolve(t.RowStatus, t.RequestedTrue, wasDefault, otherActive);
    }
}
