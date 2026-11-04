using Ecommerce.Application.Abstractions;
using Ecommerce.Application.Features.Authen.Mapping;
using Ecommerce.Domain.Authen;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Domain.Authen.ValueObjects;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;

namespace Ecommerce.Application.Features.Authen.Login;

public sealed class LoginCommandHandler(
    IUserRepository users,
    IPasswordRepository passwords,
    ITokenRepository tokens,
    IPasswordHasher hasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork) : ICommandHandler<LoginCommand, LoginResponse>
{
    // Verified when the user does not exist, so an unknown user costs the same time as a wrong password.
    private static readonly PasswordCredential Decoy = new(new byte[32], new byte[16]);

    public async Task<Result<LoginResponse>> HandleAsync(LoginCommand request, CancellationToken cancellationToken)
    {
        var login = LoginIdentifier.Create(request.UsernameOrEmail).Value;

        var user = await users.FindByLoginAsync(login, cancellationToken);
        var credential = user is null ? null : await passwords.GetActiveAsync(user.Id, cancellationToken);

        var verified = hasher.Verify(request.Password, credential ?? Decoy);

        if (user is null || credential is null || !verified)
        {
            return AuthenErrors.InvalidCredentials;
        }

        var canSignIn = user.EnsureCanSignIn();
        if (canSignIn.IsFailure)
        {
            return Result<LoginResponse>.Failure(canSignIn.Errors);
        }

        var access = tokenService.IssueAccessToken(user.Id, user.Username);
        var refresh = tokenService.IssueRefreshToken();

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await tokens.SaveAccessTokenAsync(user.Id, tokenService.Hash(access.Value), access.ExpiresAtUtc, ct);
            await tokens.SaveRefreshTokenAsync(user.Id, tokenService.Hash(refresh.Value), refresh.ExpiresAtUtc, ct);
        }, cancellationToken);

        return user.ToLoginResponse(access, refresh);
    }
}
