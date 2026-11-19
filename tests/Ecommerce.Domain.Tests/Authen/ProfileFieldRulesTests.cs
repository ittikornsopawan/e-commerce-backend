using Ecommerce.Domain.Common;

namespace Ecommerce.Domain.Tests.Authen;

// Spec 07 sequence validation note (TC-PROFILE-006).
public sealed class ProfileFieldRulesTests
{
    [Theory]
    [InlineData("12345678", true)]
    [InlineData("+123456789012345", true)]
    [InlineData("1234567", false)]
    [InlineData("+1234567890123456", false)]
    [InlineData("08-1234", false)]
    [InlineData("++12345678", false)]
    public void TC_PROFILE_006_phone_number(string value, bool valid) => Assert.Equal(valid, ProfileFieldRules.IsPhoneNumber(value));

    [Theory]
    [InlineData("2026-11-19", true)]
    [InlineData("2026-11-20", false)]
    [InlineData("1990-02-30", false)]
    [InlineData("31-01-1990", false)]
    [InlineData("2000-02-29", true)]
    public void TC_PROFILE_006_date_of_birth_is_real_and_not_after_today(string value, bool valid) =>
        Assert.Equal(valid, ProfileFieldRules.IsDateOfBirth(value, new DateOnly(2026, 11, 19)));
}
