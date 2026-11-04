using Ecommerce.Domain.Common;
using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen.ValueObjects;

public sealed class PersonName : ValueObject
{
    public const int MaxPartLength = 64;

    private PersonName(string first, string last)
    {
        First = first;
        Last = last;
    }

    public string First { get; }

    public string Last { get; }

    public string Full => $"{First} {Last}";

    public static Result<PersonName> Create(string? first, string? last)
    {
        var errors = new List<Error>();
        var firstName = (first ?? string.Empty).Trim();
        var lastName = (last ?? string.Empty).Trim();

        if (firstName.Length is 0 or > MaxPartLength)
        {
            errors.Add(Error.Validation("FirstName.Invalid", $"FirstName is required and at most {MaxPartLength} characters."));
        }

        if (lastName.Length is 0 or > MaxPartLength)
        {
            errors.Add(Error.Validation("LastName.Invalid", $"LastName is required and at most {MaxPartLength} characters."));
        }

        return errors.Count > 0 ? Result<PersonName>.Failure(errors) : new PersonName(firstName, lastName);
    }

    public static PersonName Restore(string first, string last) => new(first, last);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return First;
        yield return Last;
    }
}
