using Ecommerce.Api.Contracts.Authen;
using Ecommerce.Api.Extensions;
using Ecommerce.Api.Security;
using Ecommerce.Application.Features.Authen.CreateNewPassword;
using Ecommerce.Application.Features.Authen.Login;
using Ecommerce.Application.Features.Authen.Logout;
using Ecommerce.Application.Features.Authen.RefreshToken;
using Ecommerce.Application.Features.Authen.Register;
using Ecommerce.Application.Features.Authen.ResetPassword;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ecommerce.Api.Controllers;

/// <summary>v1/auth APIs of the authen-and-author specs.</summary>
[ApiController]
[Route("v1/auth")]
public sealed class AuthController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Spec 01-register: 201 S10000 with data.userId.</summary>
    [EnableRateLimiting(RateLimiting.Strict)]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(
            new RegisterCommand(
                request.Username, request.EmailAddress, request.Password, request.FirstName, request.LastName, request.DisplayName,
                request.ProfilePicturePath, request.PhoneNumber, request.Address, request.DateOfBirth, request.Gender, request.ReferrerId,
                request.Addresses is { } a ? new RegisterAddressInput(a.Title, a.IsDefault, a.Address, a.City, a.State, a.PostalCode, a.Country) : null,
                request.Consents?.Select(c => new RegisterConsentInput(c.ConsentId, c.ConsentVersion, c.Result)).ToList()),
            cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess(StatusCodes.Status201Created, "Created", "Register Success", result.Value)
            : result.ToFailure(this);
    }

    /// <summary>Spec 02-login: 200 S10000 with the token pair; every failure is the same 401 E10002.</summary>
    [EnableRateLimiting(RateLimiting.Strict)]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new LoginCommand(request.Username, request.Password), cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Logged-in Success", result.Value)
            : result.ToFailure(this);
    }

    /// <summary>Spec 03-reset-password: the same 200 for every business outcome, status only.</summary>
    [EnableRateLimiting(RateLimiting.Strict)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(RequestPasswordResetBody request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RequestPasswordResetCommand(request.EmailAddress), cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess<object>(StatusCodes.Status200OK, "Ok", "If the email address is registered, a reset link has been sent.", null)
            : result.ToFailure(this);
    }

    /// <summary>Spec 04-create-new-password: 200 S10000 status only; every token problem is one 400.</summary>
    [EnableRateLimiting(RateLimiting.Strict)]
    [HttpPost("create-new-password")]
    public async Task<IActionResult> CreateNewPassword(CreateNewPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new CreateNewPasswordCommand(request.ResetToken, request.NewPassword), cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess<object>(StatusCodes.Status200OK, "Ok", "Create New Password Success", null)
            : result.ToFailure(this);
    }

    /// <summary>Spec 05-refresh-token: 200 S10000 with a fresh pair in the same session; every failure is one 401.</summary>
    [EnableRateLimiting(RateLimiting.Standard)]
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RefreshTokenCommand(request.RefreshAccessToken), cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Refresh Token Success", result.Value)
            : result.ToFailure(this);
    }

    /// <summary>
    /// Spec 06-logout: 200 S10000 status only. Validation (400) comes before the credential (401), so the JWT is checked
    /// here instead of by [Authorize], which would answer 401 before the header filter runs.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > 0 || Request.Headers.TransferEncoding.Count > 0)
            return Result.Failure(Error.Validation("Logout.Body", "Timestamp must be a valid UTC timestamp and the request body must be empty.")).ToFailure(this);

        var header = Request.Headers.Authorization.ToString();
        if (User.Identity?.IsAuthenticated != true || !User.TryGetUserId(out var userId) || !User.TryGetExpiresAt(out var expiresAt)
            || !header.StartsWith("Bearer ", StringComparison.Ordinal))
            return Result.Failure(Ecommerce.Domain.Authen.AuthenErrors.AccessTokenInvalid).ToFailure(this);

        var result = await dispatcher.SendAsync(new LogoutCommand(userId, header["Bearer ".Length..], expiresAt), cancellationToken);
        return result.IsSuccess
            ? this.ToSuccess<object>(StatusCodes.Status200OK, "Ok", "Logout Success", null)
            : result.ToFailure(this);
    }
}
