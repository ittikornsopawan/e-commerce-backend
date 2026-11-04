using Ecommerce.Domain.Common;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.ValueObjects;

public sealed class DisplayName : ValueObject
{
    public const int MaxLength = 128;

    private DisplayName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>Uses <paramref name="raw"/> when given, otherwise the person's full name.</summary>
    public static Result<DisplayName> Create(string? raw, PersonName fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            var full = fallback.Full;
            return new DisplayName(full.Length <= MaxLength ? full : full[..MaxLength]);
        }

        var value = raw.Trim();
        return value.Length <= MaxLength
            ? new DisplayName(value)
            : Error.Validation("DisplayName.Invalid", $"DisplayName is at most {MaxLength} characters.");
    }

    public static DisplayName Restore(string value) => new(value);

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
