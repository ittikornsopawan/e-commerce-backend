using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.Login;

public sealed class LoginCommandValidator : IValidator<LoginCommand>
{
    public IReadOnlyList<Error> Validate(LoginCommand request)
    {
        var errors = new List<Error>();

        var login = LoginIdentifier.Create(request.UsernameOrEmail);
        if (login.IsFailure)
        {
            errors.AddRange(login.Errors);
        }

        // Only presence and length: the password policy applies when a password is set, not when it is used.
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > PlainPassword.MaxLength)
        {
            errors.Add(Error.Validation("Password.Invalid", "Password is required."));
        }

        return errors;
    }
}
