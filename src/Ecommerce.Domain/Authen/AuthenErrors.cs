using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen;

public static class AuthenErrors
{
    public static readonly Error UsernameTaken =
        Error.Conflict("Authen.UsernameTaken", "This username is already taken.");

    public static readonly Error EmailTaken =
        Error.Conflict("Authen.EmailTaken", "This email is already registered.");

    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Authen.InvalidCredentials", "Username or password is incorrect.");

    public static readonly Error AccountNotActive =
        Error.Forbidden("Authen.AccountNotActive", "This account is not active.");

    public static readonly Error InvalidResetToken =
        Error.Validation("Authen.InvalidResetToken", "The reset token is invalid or has expired.");

    public static readonly Error UserNotFound =
        Error.NotFound("Authen.UserNotFound", "User was not found.");
}
