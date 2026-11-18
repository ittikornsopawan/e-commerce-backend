using Ecommerce.Application.Features.Authen.GetCurrentUser;
using Ecommerce.Domain.Authen.Entities;

namespace Ecommerce.Application.Features.Authen.Mapping;

/// <summary>
/// Domain objects never leave the Application layer: handlers map them to the response records with these methods.
/// Written by hand on purpose, so a renamed property breaks the build instead of silently mapping to nothing.
/// </summary>
internal static class AuthenMappings
{
    public static CurrentUserResponse ToCurrentUserResponse(this User user) =>
        new(
            user.Id.Value,
            user.Username.Value,
            user.Email.Value,
            user.Name.First,
            user.Name.Last,
            user.DisplayName.Value,
            user.ProfilePicturePath);
}
