using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.Logout;

/// <summary>
/// POST v1/auth/logout (spec 06). Built only from the verified JWT: its sub, the exact presented token and its exp.
/// </summary>
public sealed record LogoutCommand(Guid UserId, string AccessToken, DateTime TokenExpiresAtUtc) : ICommand;
