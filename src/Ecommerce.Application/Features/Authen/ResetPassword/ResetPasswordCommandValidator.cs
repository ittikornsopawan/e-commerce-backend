using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.ResetPassword;

public sealed class ResetPasswordCommandValidator : IValidator<ResetPasswordCommand>
{
    public IReadOnlyList<Error> Validate(ResetPasswordCommand request)
    {
        var errors = new List<Error>();

        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 256)
        {
            errors.Add(AuthenErrors.InvalidResetToken);
        }

        var password = PlainPassword.Create(request.NewPassword);
        if (password.IsFailure)
        {
            errors.AddRange(password.Errors);
        }

        return errors;
    }
}
