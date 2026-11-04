using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.ResetPassword;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand;
