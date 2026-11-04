using Ecommerce.Domain.Common;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>What a user types into the login box: a username or an email address, trimmed and lower-case.</summary>
public sealed class LoginIdentifier : ValueObject
{
    private LoginIdentifier(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<LoginIdentifier> Create(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();

        return value.Length is > 0 and <= 254
            ? new LoginIdentifier(value)
            : Error.Validation("UsernameOrEmail.Invalid", "Username or email is required.");
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
