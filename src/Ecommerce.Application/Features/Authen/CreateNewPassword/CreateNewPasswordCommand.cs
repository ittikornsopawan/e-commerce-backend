using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.CreateNewPassword;

/// <summary>POST v1/auth/create-new-password (spec 04-create-new-password). The reset token is the proof; no user id is accepted.</summary>
public sealed record CreateNewPasswordCommand(string? ResetToken, string? NewPassword) : ICommand;
