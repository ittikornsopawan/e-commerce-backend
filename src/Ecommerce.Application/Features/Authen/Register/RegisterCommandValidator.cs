using Ecommerce.Application.Common;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.Register;

/// <summary>The rules live in the domain value objects; this only asks them.</summary>
public sealed class RegisterCommandValidator : IValidator<RegisterCommand>
{
    public IReadOnlyList<Error> Validate(RegisterCommand request)
    {
        var name = PersonName.Create(request.FirstName, request.LastName);

        return ValidationErrors.From(
            Username.Create(request.Username),
            EmailAddress.Create(request.Email),
            PlainPassword.Create(request.Password),
            name,
            name.IsSuccess ? DisplayName.Create(request.DisplayName, name.Value) : Result.Success());
    }
}
