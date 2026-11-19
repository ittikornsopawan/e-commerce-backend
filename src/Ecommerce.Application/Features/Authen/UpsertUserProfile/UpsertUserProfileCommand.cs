using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.UpsertUserProfile;

/// <summary>
/// POST v1/user/profile/upsert (spec 07). Each field is unset (keep), or set to a value or null (clear). The caller is
/// null when the JWT did not verify; the handler answers 401 after the validator's 400s.
/// </summary>
public sealed record UpsertUserProfileCommand(
    Caller? Caller,
    Optional<string?> FirstName,
    Optional<string?> LastName,
    Optional<string?> DisplayName,
    Optional<string?> ProfilePicturePath,
    Optional<string?> PhoneNumber,
    Optional<string?> Address,
    Optional<string?> DateOfBirth,
    Optional<string?> Gender) : ICommand<UpsertUserProfileResponse>;

public sealed record UpsertUserProfileResponse(Guid UserId);
