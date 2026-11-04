using Ecommerce.Application.Common;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.ResetPassword;

public sealed class RequestPasswordResetCommandValidator : IValidator<RequestPasswordResetCommand>
{
    public IReadOnlyList<Error> Validate(RequestPasswordResetCommand request) =>
        ValidationErrors.From(EmailAddress.Create(request.Email));
}
