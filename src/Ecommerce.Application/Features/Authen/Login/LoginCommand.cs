using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.Login;

public sealed record LoginCommand(string UsernameOrEmail, string Password) : ICommand<LoginResponse>;
