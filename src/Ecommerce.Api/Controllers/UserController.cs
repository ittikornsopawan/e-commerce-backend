using System.Text.Json;
using Ecommerce.Api.Contracts;
using Ecommerce.Api.Contracts.Authen;
using Ecommerce.Api.Extensions;
using Ecommerce.Api.Security;
using Ecommerce.Application.Features.Authen.GetUserProfile;
using Ecommerce.Application.Features.Authen.InquiryUserAddress;
using Ecommerce.Application.Features.Authen.UpsertUserAddress;
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
            VerifiedCaller.Of(this), p.String("firstName"), p.String("lastName"), p.String("displayName"), p.String("profilePicturePath"),
            p.String("phoneNumber"), p.String("address"), p.String("dateOfBirth"), p.String("gender")), cancellationToken);

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

    /// <summary>Spec 09-upsert-user-address: insert 201, update 200 (data.userAddressId), delete 204 with no body.</summary>
    [HttpPost("address/upsert")]
    public async Task<IActionResult> UpsertAddress([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (JsonBody.TryRead(body, AddressBody.Fields, out var b) is { } shapeError)
            return Result.Failure(Error.Validation("Address.Shape", shapeError)).ToFailure(this);

        var result = await dispatcher.SendAsync(new UpsertUserAddressCommand(
            VerifiedCaller.Of(this), b.String("id"), b.String("title").Value, b.Boolean("isDefault"), b.String("address").Value,
            b.String("city").Value, b.String("state").Value, b.String("postalCode").Value, b.String("country").Value, b.String("rowStatus")),
            cancellationToken);

        if (result.IsFailure) return result.ToFailure(this);
        var data = new { userAddressId = result.Value.UserAddressId };
        return result.Value.Action switch
        {
            AddressAction.Insert => this.ToSuccess(StatusCodes.Status201Created, "Created", "Insert User Address Success", data),
            AddressAction.Update => this.ToSuccess(StatusCodes.Status200OK, "Ok", "Update User Address Success", data),
            _ => NoContent()
        };
    }

    private static readonly HashSet<string> AddressQueryKeys = ["id", "pageIndex", "pageSize", "search", "rowStatus"];

    /// <summary>Spec 10-inquiry-user-address: 200 S10000 with items and the four pagination totals.</summary>
    [HttpGet("address")]
    public async Task<IActionResult> GetAddresses(CancellationToken cancellationToken)
    {
        if (QueryParameters.TryRead(Request, AddressQueryKeys, out var q) is { } shapeError)
            return Result.Failure(Error.Validation("Address.Request", shapeError)).ToFailure(this);

        var result = await dispatcher.SendAsync(new InquiryUserAddressQuery(
            VerifiedCaller.Of(this), q.GetValueOrDefault("id"), q.GetValueOrDefault("pageIndex"), q.GetValueOrDefault("pageSize"),
            q.GetValueOrDefault("search"), q.GetValueOrDefault("rowStatus")), cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Inquiry User Address Success", result.Value)
            : result.ToFailure(this);
    }
}
