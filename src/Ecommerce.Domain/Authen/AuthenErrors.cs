using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen;

public static class AuthenErrors
{
    public static readonly Error UsernameTaken =
        Error.Conflict("Authen.UsernameTaken", "This username is already taken.");

    public static readonly Error EmailTaken =
        Error.Conflict("Authen.EmailTaken", "This email is already registered.");

    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Authen.InvalidCredentials", "Invalid username or password."); // spec 02-login: one answer for every failure

    public static readonly Error AccountNotActive =
        Error.Forbidden("Authen.AccountNotActive", "This account is not active.");

    public static readonly Error InvalidResetToken =
        Error.Validation("Authen.InvalidResetToken", "The reset token is invalid or has expired.");

    public static readonly Error UserNotFound =
        Error.NotFound("Authen.UserNotFound", "User was not found.");

    // Spec 01-register messages.
    public static readonly Error RequiredFieldsMissing =
        Error.Validation("Authen.RequiredFields", "Username, Email Address and Password are required fields.");

    public static readonly Error IdentifierTaken =
        Error.Conflict("Authen.IdentifierTaken", "Username or email address already exists.");

    public static readonly Error ConsentOrReferrerNotFound =
        Error.Of("Authen.ReferenceNotFound", "Consent or referrer not found.", ErrorType.NotFound, "E10005");

    public static readonly Error ConsentVersionNotCurrent =
        Error.Validation("Authen.ConsentVersion", "Consent version is no longer current.");

    public static readonly Error RequiredConsentNotAccepted =
        Error.Validation("Authen.RequiredConsent", "All current required consents must be accepted.");
}
