using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.Mapping;
using Ecommerce.Domain;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Entities;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.Register;

public sealed class RegisterCommandHandler(
    IUserRepository users,
    IPasswordRepository passwords,
    IPasswordHasher hasher,
    IUnitOfWork unitOfWork) : ICommandHandler<RegisterCommand, RegisterResponse>
{
    public async Task<Result<RegisterResponse>> HandleAsync(RegisterCommand request, CancellationToken cancellationToken)
    {
        // The dispatcher has already run RegisterCommandValidator, so these cannot fail here.
        var username = Username.Create(request.Username).Value;
        var email = EmailAddress.Create(request.Email).Value;
        var name = PersonName.Create(request.FirstName, request.LastName).Value;
        var displayName = DisplayName.Create(request.DisplayName, name).Value;
        var password = PlainPassword.Create(request.Password).Value;

        if (await users.UsernameExistsAsync(username, cancellationToken))
        {
            return AuthenErrors.UsernameTaken;
        }

        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            return AuthenErrors.EmailTaken;
        }

        var user = User.Register(UserId.New(), username, email, name, displayName);
        var credential = hasher.Hash(password.Value);

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await users.AddAsync(user, ct);
                await passwords.SetAsync(user.Id, credential, ct);
            }, cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            // Another request registered the same username/email between the checks above and the insert.
            return AuthenErrors.UsernameTaken;
        }

        return user.ToRegisterResponse();
    }
}
