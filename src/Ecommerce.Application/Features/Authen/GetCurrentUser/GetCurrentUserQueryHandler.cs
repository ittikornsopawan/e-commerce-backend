using Ecommerce.Application.Features.Authen.Mapping;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler(IUserRepository users)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> HandleAsync(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(new UserId(request.UserId), cancellationToken);

        return user is null || !user.IsActive
            ? AuthenErrors.UserNotFound
            : user.ToCurrentUserResponse();
    }
}
