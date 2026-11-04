namespace Ecommerce.Application.Features.Authen.GetCurrentUser;

public sealed record CurrentUserResponse(
    Guid UserId,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string DisplayName,
    string? ProfilePicturePath);
