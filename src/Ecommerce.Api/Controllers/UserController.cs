using System.Text.Json;
using Ecommerce.Api.Contracts.Authen;
using Ecommerce.Api.Extensions;
using Ecommerce.Api.Security;
using Ecommerce.Application.Features.Authen.GetUserProfile;
using Ecommerce.Application.Features.Authen.UpsertUserProfile;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Controllers;

/// <summary>v1/user APIs of the authen-and-author specs (the caller's own profile and addresses).</summary>
[ApiController]
[Route("v1/user")]
public sealed class UserController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Spec 07-upsert-user-profile: 200 S10000 with data.userId.</summary>
    [HttpPost("profile/upsert")]
    public async Task<IActionResult> UpsertProfile([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (ProfileBody.TryRead(body, out var p) is { } shapeError)
            return Result.Failure(Error.Validation("Profile.Shape", shapeError)).ToFailure(this);

        var result = await dispatcher.SendAsync(new UpsertUserProfileCommand(
            VerifiedCaller.Of(this), p["firstName"], p["lastName"], p["displayName"], p["profilePicturePath"],
            p["phoneNumber"], p["address"], p["dateOfBirth"], p["gender"]), cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Upsert User Profile Success", result.Value)
            : result.ToFailure(this);
    }

    /// <summary>Spec 08-inquiry-user-profile: 200 S10000 with the caller's profile; no query parameters and no body.</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (Request.QueryString.HasValue || Request.ContentLength > 0 || Request.Headers.TransferEncoding.Count > 0)
            return Result.Failure(Error.Validation("Profile.Request", "Timestamp must be valid UTC and this endpoint accepts no query parameters or body.")).ToFailure(this);

        var result = await dispatcher.SendAsync(new GetUserProfileQuery(VerifiedCaller.Of(this)), cancellationToken);
        return result.IsSuccess
            ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Inquiry User Profile Success", result.Value)
            : result.ToFailure(this);
    }
}
