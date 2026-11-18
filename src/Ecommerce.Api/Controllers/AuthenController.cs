using Ecommerce.Api.Contracts.Authen;
using Ecommerce.Api.Extensions;
using Ecommerce.Application.Features.Authen.GetCurrentUser;
using Ecommerce.Application.Features.Authen.ResetPassword;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using Microsoft.AspNetCore.Authorization;
using Ecommerce.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ecommerce.Api.Controllers;

[ApiController]
[Route("api/authen")]
public sealed class AuthenController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Always answers 202, whether or not the email is registered.</summary>
    [EnableRateLimiting(RateLimiting.Strict)]
    [HttpPost("password-reset/request")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestPasswordReset(RequestPasswordResetRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new RequestPasswordResetCommand(request.Email), cancellationToken);
        return result.IsSuccess ? Accepted() : result.ToFailure(this);
    }

    [EnableRateLimiting(RateLimiting.Strict)]
    [HttpPost("password-reset/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(
            new ResetPasswordCommand(request.Token, request.NewPassword), cancellationToken);

        return result.IsSuccess ? NoContent() : result.ToFailure(this);
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var subject = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return Result.Failure(Error.Unauthorized("Token.Invalid", "Access token is invalid or expired.")).ToFailure(this);
        }

        var result = await dispatcher.SendAsync(new GetCurrentUserQuery(userId), cancellationToken);
        return result.IsSuccess ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Success", result.Value) : result.ToFailure(this);
    }
}
