using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Authen;

public static class AuthenErrors
{
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Authen.InvalidCredentials", "Invalid username or password."); // spec 02-login: one answer for every failure

    public static readonly Error InvalidResetToken =
        Error.Validation("Authen.InvalidResetToken", "Reset token is invalid or expired."); // spec 04: one answer for every token case

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

    // Spec 04-create-new-password.
    public static readonly Error SamePassword =
        Error.Validation("Authen.SamePassword", "New password must be different from the current password.");

    // Spec 05-refresh-token: one answer for every authentication failure.
    public static readonly Error RefreshTokenInvalid =
        Error.Unauthorized("Authen.RefreshTokenInvalid", "Invalid or expired refresh token.");

    // Spec 06-logout (and every protected API): one answer for every credential failure.
    public static readonly Error AccessTokenInvalid =
        Error.Unauthorized("Authen.AccessTokenInvalid", "Invalid or expired access token.");

    // Spec 09-upsert-user-address.
    public static readonly Error AddressNotFound =
        Error.NotFound("Authen.AddressNotFound", "User address not found.");

    public static Error AddressLimit(int max) =>
        Error.Validation("Authen.AddressLimit", $"A user can have at most {max} addresses.");
}
