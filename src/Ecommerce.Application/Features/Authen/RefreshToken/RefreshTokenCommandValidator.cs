using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Authen.RefreshToken;

/// <summary>Spec 05: refreshAccessToken is a mandatory non-empty string, compared exactly without trimming.</summary>
public sealed class RefreshTokenCommandValidator : IValidator<RefreshTokenCommand>
{
    public IReadOnlyList<Error> Validate(RefreshTokenCommand request) =>
        string.IsNullOrEmpty(request.RefreshAccessToken) ? [Error.Validation("Refresh.Invalid", Required("refreshAccessToken"))] : [];
}
