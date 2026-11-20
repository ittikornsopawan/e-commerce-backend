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
    List<RegisterConsentRequest>? Consents) : IEncryptedRequest
{
    public IEncryptedRequest Decrypt(EncryptedFields f) => this with
    {
        Username = f.Field("username", Username),
        EmailAddress = f.Field("emailAddress", EmailAddress),
        Password = f.Field("password", Password),
        FirstName = f.Field("firstName", FirstName),
        LastName = f.Field("lastName", LastName),
        PhoneNumber = f.Field("phoneNumber", PhoneNumber),
        Address = f.Field("address", Address),
        Addresses = Addresses?.Decrypt(f)
    };
}

public sealed record RegisterAddressRequest(
    string? Title, bool? IsDefault, string? Address, string? City, string? State, string? PostalCode, string? Country)
{
    public RegisterAddressRequest Decrypt(EncryptedFields f) => this with
    {
        Address = f.Field("addresses.address", Address),
        City = f.Field("addresses.city", City),
        State = f.Field("addresses.state", State),
        PostalCode = f.Field("addresses.postalCode", PostalCode),
        Country = f.Field("addresses.country", Country)
    };
}

public sealed record RegisterConsentRequest(string? ConsentId, string? ConsentVersion, bool? Result);
