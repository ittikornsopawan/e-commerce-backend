using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.GetUserProfile;

/// <summary>GET v1/user/profile (spec 08). The caller is null when the JWT did not verify.</summary>
public sealed record GetUserProfileQuery(Caller? Caller) : IQuery<UserProfileResponse>;

/// <summary>data of spec 08: every field is present; unset ones are null.</summary>
public sealed record UserProfileResponse(
    Guid UserId, string EmailAddress, string? FirstName, string? LastName, string? DisplayName, string? ProfilePicturePath,
    string? PhoneNumber, string? Address, DateOnly? DateOfBirth, string? Gender);
