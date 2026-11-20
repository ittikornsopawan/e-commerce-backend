using Ecommerce.Shared.Validation;

namespace Ecommerce.Api.Tests;

// api-conventions.md "Validation messages" (Josh, 2026-11-18).
public sealed class ValidationMessagesTests
{
    [Theory]
    [InlineData("emailAddress", "Email Address")]
    [InlineData("referrerId", "Referrer Id")]
    [InlineData("username", "Username")]
    public void Field_is_title_case_words(string field, string expected) => Assert.Equal(expected, ValidationMessages.Field(field));

    [Fact]
    public void Forms_match_the_conventions()
    {
        Assert.Equal("Username, Email Address and Password are required fields.", ValidationMessages.Required("username", "emailAddress", "password"));
        Assert.Equal("Username is a required field.", ValidationMessages.Required("username"));
        Assert.Equal("Gender must be MALE, FEMALE or OTHER.", ValidationMessages.OneOf("gender", "MALE", "FEMALE", "OTHER"));
        Assert.Equal("consents[1]: Consent Id must not be duplicated.", ValidationMessages.At("consents[1]", ValidationMessages.Duplicated("consentId")));
        Assert.Equal("a, b.", ValidationMessages.Combine(["a.", "b."]));
    }
}
