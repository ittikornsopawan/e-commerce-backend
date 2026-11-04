using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Authen.GetCurrentUser;

public sealed record GetCurrentUserQuery(Guid UserId) : IQuery<CurrentUserResponse>;
