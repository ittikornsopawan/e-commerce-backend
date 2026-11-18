using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Authen.Login;

/// <summary>
/// Spec 02-login input rules: username non-empty and at most 254 characters, password non-empty and at most 128.
/// The registration password rule is not applied to a login attempt.
/// </summary>
public sealed class LoginCommandValidator : IValidator<LoginCommand>
{
    public IReadOnlyList<Error> Validate(LoginCommand r)
    {
        var messages = new List<string>();
        var missing = new[] { ("username", r.Username), ("password", r.Password) }.Where(f => string.IsNullOrEmpty(f.Item2)).Select(f => f.Item1).ToArray();
        if (missing.Length > 0) messages.Add(Required(missing));
        if (r.Username is { Length: > 254 }) messages.Add(MaxLength("username", 254));
        if (r.Password is { Length: > 128 }) messages.Add(MaxLength("password", 128));

        return messages.Count == 0 ? [] : [Error.Validation("Login.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
