using Ecommerce.Domain.Common;

namespace Ecommerce.Domain.Tests.Authen;

// api-conventions.md "Email address" (owner decision 2026-11-18): the examples of the rule and its boundaries.
public sealed class EmailAddressFormatTests
{
    [Theory]
    [InlineData("a.b@shop.co")]
    [InlineData("a@shop.co")]
    [InlineData("A.B@Shop.CO")] // validity only: the case is kept as sent
    [InlineData("a+tag@mail.shop.co")]
    public void Plain_addresses_with_a_dot_in_the_domain_are_valid(string value) => Assert.True(EmailAddressFormat.IsValid(value));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a@b@c")]
    [InlineData("a.b@c")] // the dot is in the local part only
    [InlineData("a b@shop.co")]
    [InlineData(" a.b@shop.co")]
    [InlineData("a.b@shop.co ")]
    [InlineData("a.b@shop.co\t")]
    [InlineData("<a@shop.co>")]
    [InlineData("A <a@shop.co>")]
    [InlineData("@shop.co")]
    [InlineData("a@")]
    public void Anything_else_is_invalid(string value) => Assert.False(EmailAddressFormat.IsValid(value));

    [Fact]
    public void The_length_limit_is_254_characters()
    {
        var domain = "@shop.co";
        Assert.True(EmailAddressFormat.IsValid(new string('a', 254 - domain.Length) + domain));
        Assert.False(EmailAddressFormat.IsValid(new string('a', 255 - domain.Length) + domain));
    }
}
