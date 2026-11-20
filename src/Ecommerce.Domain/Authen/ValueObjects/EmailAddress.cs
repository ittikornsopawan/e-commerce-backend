using Ecommerce.Domain.Common;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>A valid address (<see cref="EmailAddressFormat"/>), trimmed and stored lower-case.</summary>
public sealed class EmailAddress : ValueObject
{
    private EmailAddress(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<EmailAddress> Create(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();

        return EmailAddressFormat.IsValid(value) ? new EmailAddress(value) : Error.Validation("Email.Invalid", "Email address is not valid.");
    }

    public static EmailAddress Restore(string value) => new(value);

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
