using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Tests.Authen;

public class ValueObjectTests
{
    [Theory]
    [InlineData("Passw0rd", true)]
    [InlineData("short1A", false)]
    [InlineData("alllowercase1", false)]
    [InlineData("ALLUPPERCASE1", false)]
    [InlineData("NoDigitsHere", false)]
    public void Password_policy(string raw, bool valid) => Assert.Equal(valid, PlainPassword.Create(raw).IsSuccess);

    [Fact]
    public void Password_does_not_reveal_itself_when_printed() =>
        Assert.DoesNotContain("Passw0rd", PlainPassword.Create("Passw0rd").Value.ToString());

    [Fact]
    public void Value_objects_are_equal_when_their_values_are()
    {
        Assert.Equal(PlainPassword.Create("Passw0rd").Value, PlainPassword.Create("Passw0rd").Value);
        Assert.NotEqual(PlainPassword.Create("Passw0rd").Value, PlainPassword.Create("Passw0rd2").Value);
    }
}
