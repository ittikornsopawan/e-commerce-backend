using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.ResetPassword;

public sealed record RequestPasswordResetCommand(string Email) : ICommand;
