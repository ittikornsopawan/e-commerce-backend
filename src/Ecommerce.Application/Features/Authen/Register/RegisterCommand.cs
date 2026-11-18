using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.Register;

/// <summary>POST v1/auth/register (spec 01-register). Formats (UUID, date) arrive as text so the validator can name them.</summary>
public sealed record RegisterCommand(
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
    RegisterAddressInput? Addresses,
    IReadOnlyList<RegisterConsentInput>? Consents) : ICommand<RegisterResponse>;

public sealed record RegisterAddressInput(
    string? Title, bool? IsDefault, string? Address, string? City, string? State, string? PostalCode, string? Country);

public sealed record RegisterConsentInput(string? ConsentId, string? ConsentVersion, bool? Result);
