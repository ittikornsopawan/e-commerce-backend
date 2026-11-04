using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Tests.Authen;

public class ValueObjectTests
{
    [Theory]
    [InlineData("Aof", "aof")]
    [InlineData("  user.name-1_ ", "user.name-1_")]
    public void Username_is_trimmed_and_lower_cased(string raw, string expected) =>
        Assert.Equal(expected, Username.Create(raw).Value.Value);

    [Theory]
    [InlineData(null)]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("a@b.com")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 33 characters
    public void Username_rejects_invalid_values(string? raw)
    {
        var result = Username.Create(raw);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
    }

    [Theory]
    [InlineData("A@Example.COM", "a@example.com")]
    public void Email_is_normalized(string raw, string expected) =>
        Assert.Equal(expected, EmailAddress.Create(raw).Value.Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("a@b")]
    [InlineData("Name <a@b.com>")]
    public void Email_rejects_invalid_values(string? raw) => Assert.True(EmailAddress.Create(raw).IsFailure);

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
        Assert.Equal(Username.Create("Aof").Value, Username.Create("aof").Value);
        Assert.NotEqual(Username.Create("aof").Value, Username.Create("bob").Value);
        Assert.Equal(new PasswordCredential([1, 2], [3]), new PasswordCredential([1, 2], [3]));
    }

    [Fact]
    public void Person_name_reports_each_bad_part()
    {
        var result = PersonName.Create("", new string('x', 65));

        Assert.Equal(["FirstName.Invalid", "LastName.Invalid"], result.Errors.Select(e => e.Code));
    }

    [Fact]
    public void Display_name_falls_back_to_the_full_name_and_enforces_the_limit()
    {
        var name = PersonName.Create("Ittikorn", "S").Value;

        Assert.Equal("Ittikorn S", DisplayName.Create(null, name).Value.Value);
        Assert.Equal("Aof", DisplayName.Create(" Aof ", name).Value.Value);
        Assert.True(DisplayName.Create(new string('x', 129), name).IsFailure);
    }
}
