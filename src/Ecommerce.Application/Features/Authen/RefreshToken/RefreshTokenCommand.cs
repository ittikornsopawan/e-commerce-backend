using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.RefreshToken;

/// <summary>POST v1/auth/refresh-token (spec 05). The user and session come only from the stored row.</summary>
public sealed record RefreshTokenCommand(string? RefreshAccessToken) : ICommand<TokenPairResponse>;
