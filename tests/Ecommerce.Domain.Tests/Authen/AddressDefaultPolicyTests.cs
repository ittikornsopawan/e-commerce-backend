using Ecommerce.Domain.Authen.Domain;

namespace Ecommerce.Domain.Tests.Authen;

// Spec 09 sequence default note (TC-ADDRESS-002, 003, 006) and quota (TC-ADDRESS-009).
public sealed class AddressDefaultPolicyTests
{
    [Theory]
    // rowStatus, requestedTrue, wasDefault, otherActiveExists, isDefault, shouldPromote
    [InlineData("ACTIVE", false, false, false, true, false)]     // the first ACTIVE address is default
    [InlineData("ACTIVE", false, false, true, false, false)]     // another ACTIVE address keeps the default
    [InlineData("ACTIVE", true, false, true, true, false)]       // explicit true selects this one
    [InlineData("ACTIVE", false, true, true, true, false)]       // false or omitted keeps a stored default
    [InlineData("INACTIVE", false, false, false, false, false)]  // a new INACTIVE address is never default
    [InlineData("INACTIVE", false, true, true, false, true)]     // a default that is deactivated hands over
    [InlineData("DELETED", false, true, true, false, true)]      // a default that is deleted hands over
    [InlineData("DELETED", false, false, true, false, false)]
    public void TC_ADDRESS_002_TC_ADDRESS_006_resolves_the_default_and_the_promotion(
        string rowStatus, bool requestedTrue, bool wasDefault, bool otherActive, bool isDefault, bool shouldPromote) =>
        Assert.Equal(new AddressDefaultDecision(isDefault, shouldPromote), AddressDefaultPolicy.Resolve(rowStatus, requestedTrue, wasDefault, otherActive));

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void TC_ADDRESS_009_ten_addresses_fill_the_quota(int count, bool full) => Assert.Equal(full, AddressQuota.IsFull(count, 10));
}
