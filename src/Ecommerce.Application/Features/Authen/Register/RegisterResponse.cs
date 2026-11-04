namespace Ecommerce.Application.Features.Authen.Register;

public sealed record RegisterResponse(Guid UserId, string Username, string DisplayName);
