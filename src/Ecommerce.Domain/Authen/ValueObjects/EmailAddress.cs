using System.Net.Mail;
using Ecommerce.Domain.Common;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>A plain address (no display name), at most 254 characters, stored lower-case.</summary>
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

        var valid = value.Length is > 0 and <= 254
            && MailAddress.TryCreate(value, out var parsed)
            && parsed.Address == value
            && value.Contains('.', StringComparison.Ordinal);

        return valid ? new EmailAddress(value) : Error.Validation("Email.Invalid", "Email address is not valid.");
    }

    public static EmailAddress Restore(string value) => new(value);

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
