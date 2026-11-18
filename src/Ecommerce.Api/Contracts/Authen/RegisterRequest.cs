namespace Ecommerce.Api.Contracts.Authen;

/// <summary>Body of POST v1/auth/register. UUID and date fields stay text so a bad value gets its spec message.</summary>
public sealed record RegisterRequest(
    string? Username,
    string? EmailAddress,
    string? Password,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? ProfilePicturePath,
    string? PhoneNumber,
    string? Address,
    string? DateOfBirth,
    string? Gender,
    string? ReferrerId,
    RegisterAddressRequest? Addresses,
    List<RegisterConsentRequest>? Consents);

public sealed record RegisterAddressRequest(
    string? Title, bool? IsDefault, string? Address, string? City, string? State, string? PostalCode, string? Country);

public sealed record RegisterConsentRequest(string? ConsentId, string? ConsentVersion, bool? Result);
