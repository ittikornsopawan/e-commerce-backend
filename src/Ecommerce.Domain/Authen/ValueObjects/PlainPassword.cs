using Ecommerce.Domain.Common;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>A new password that passed the password policy. Only used when a password is set; logging in never applies the policy.</summary>
public sealed class PlainPassword : ValueObject
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    private PlainPassword(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<PlainPassword> Create(string? raw)
    {
        if (raw is null || raw.Length < MinLength || raw.Length > MaxLength)
        {
            return Error.Validation("Password.Length", $"Password must be {MinLength}-{MaxLength} characters.");
        }

        if (!raw.Any(char.IsUpper) || !raw.Any(char.IsLower) || !raw.Any(char.IsDigit))
        {
            return Error.Validation("Password.Weak", "Password must contain an upper-case letter, a lower-case letter and a digit.");
        }

        return new PlainPassword(raw);
    }

    public override string ToString() => "********";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
