namespace Ecommerce.Domain.Authen.Domain;

/// <summary>The stored default flag of an address and whether another address must take over (spec 09).</summary>
public sealed record AddressDefaultDecision(bool IsDefault, bool ShouldPromote);

/// <summary>
/// Default-address rules of spec 09-upsert-user-address (sequence default note). Only an ACTIVE address can be the
/// default; an explicit true selects it, a stored default stays, and the first ACTIVE address always becomes default.
/// </summary>
public static class AddressDefaultPolicy
{
    public const string Active = "ACTIVE";

    /// <summary>Resolves the flag to store; promotion is needed when the stored default leaves ACTIVE.</summary>
    public static AddressDefaultDecision Resolve(string rowStatus, bool requestedTrue, bool wasDefault, bool otherActiveExists)
    {
        var isActive = rowStatus == Active;
        var isDefault = isActive && (requestedTrue || wasDefault || !otherActiveExists);
        return new AddressDefaultDecision(isDefault, ShouldPromote: wasDefault && !isActive);
    }
}

/// <summary>Spec 09: a user may keep at most <c>max</c> non-DELETED addresses (10 by default); only inserts are limited.</summary>
public static class AddressQuota
{
    public static bool IsFull(int nonDeletedAddresses, int max) => nonDeletedAddresses >= max;
}
