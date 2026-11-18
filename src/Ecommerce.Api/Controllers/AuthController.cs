using Ecommerce.Api.Contracts.Authen;
using Ecommerce.Api.Extensions;
using Ecommerce.Api.Security;
using Ecommerce.Application.Features.Authen.Login;
using Ecommerce.Application.Features.Authen.Register;
using Ecommerce.Application.Features.Authen.ResetPassword;
using Ecommerce.Shared.Cqrs;
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
}
