namespace Ecommerce.Domain.Authen.ValueObjects;

/// <summary>Everything one registration stores (spec 01-register inserts). Text is plain here; storage encrypts it.</summary>
public sealed record NewRegistration(
    string Username,
    string EmailAddress,
    PasswordCredential Password,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? ProfilePicturePath,
    string? PhoneNumber,
    string? Address,
    DateOnly? DateOfBirth,
    string? Gender,
    Guid? ReferrerId,
    RegistrationAddress? DefaultAddress,
    IReadOnlyList<SubmittedConsent> Consents,
    DateTime NowUtc);

public sealed record RegistrationAddress(string Title, string Address, string City, string State, string PostalCode, string Country);
