using Ecommerce.Domain.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Authen.ResetPassword;

/// <summary>
/// Spec 03-reset-password: emailAddress required, a valid email address, at most 254 characters. The spec example message
/// covers missing and invalid (a spec example keeps its text); the length uses the convention form.
/// </summary>
public sealed class RequestPasswordResetCommandValidator : IValidator<RequestPasswordResetCommand>
{
    public const string RequiredAndValid = "Email Address is required and must be a valid email address.";

    public IReadOnlyList<Error> Validate(RequestPasswordResetCommand request) => request.EmailAddress switch
    {
        null or "" => [Invalid(RequiredAndValid)],
        { Length: > 254 } => [Invalid(MaxLength("emailAddress", 254))],
        var email when !EmailAddressFormat.IsValid(email) => [Invalid(RequiredAndValid)],
        _ => []
    };

    private static Error Invalid(string message) => Error.Validation("ResetRequest.Invalid", message);
}
