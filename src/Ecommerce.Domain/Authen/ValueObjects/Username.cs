using System.Text.RegularExpressions;
using Ecommerce.Domain.Common;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>3-32 characters of letters, digits, '.', '_' or '-'. Stored lower-case, so usernames are case-insensitive.</summary>
public sealed partial class Username : ValueObject
{
    private Username(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<Username> Create(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();

        return Pattern().IsMatch(value)
            ? new Username(value)
            : Error.Validation("Username.Invalid", "Username must be 3-32 characters: letters, digits, '.', '_' or '-'.");
    }

    /// <summary>Rebuilds a value that was valid when it was stored. No rules are applied.</summary>
    public static Username Restore(string value) => new(value);

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    [GeneratedRegex("^[a-z0-9._-]{3,32}$")]
    private static partial Regex Pattern();
}
