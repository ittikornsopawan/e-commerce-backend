using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.ResetPassword;

/// <summary>POST v1/auth/reset-password (spec 03-reset-password).</summary>
public sealed record RequestPasswordResetCommand(string? EmailAddress) : ICommand;
