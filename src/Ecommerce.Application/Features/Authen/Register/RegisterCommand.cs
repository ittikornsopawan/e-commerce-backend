using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.Register;

public sealed record RegisterCommand(
    string Username,
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? DisplayName) : ICommand<RegisterResponse>;
