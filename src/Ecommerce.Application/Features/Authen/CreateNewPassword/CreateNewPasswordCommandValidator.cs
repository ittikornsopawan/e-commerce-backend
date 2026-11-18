using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Authen.CreateNewPassword;

/// <summary>Spec 04: both fields required, then the password policy, all before any reset lookup. Passwords are never trimmed.</summary>
public sealed class CreateNewPasswordCommandValidator : IValidator<CreateNewPasswordCommand>
{
    public IReadOnlyList<Error> Validate(CreateNewPasswordCommand r)
    {
        var missing = new[] { ("resetToken", r.ResetToken), ("newPassword", r.NewPassword) }.Where(f => string.IsNullOrEmpty(f.Item2)).Select(f => f.Item1).ToArray();
        if (missing.Length > 0) return [Error.Validation("CreateNewPassword.Invalid", Required(missing))];
        return PlainPassword.Create(r.NewPassword) is { IsFailure: true } policy ? [policy.FirstError] : [];
    }
}
