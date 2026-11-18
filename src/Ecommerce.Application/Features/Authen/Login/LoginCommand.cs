using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.Login;

/// <summary>POST v1/auth/login (spec 02-login). <c>Username</c> is the username or the email address, exactly as sent.</summary>
public sealed record LoginCommand(string? Username, string? Password) : ICommand<LoginResponse>;
