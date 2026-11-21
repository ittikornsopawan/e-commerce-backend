using System.Text.Json;
using Ecommerce.Api.Contracts;
using Ecommerce.Api.Contracts.Author;
using Ecommerce.Api.Extensions;
using Ecommerce.Api.Security;
using Ecommerce.Application.Features.Author.InquiryRole;
using Ecommerce.Application.Features.Author.UpsertRole;
using Ecommerce.Application.Features.Author.UpsertRoleBatch;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using Ecommerce.Shared.Validation;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Controllers;

/// <summary>v1/author APIs of the author specs (roles, permissions, scopes and their grants); platform administrators only.</summary>
[ApiController]
[Route("v1/author")]
public sealed class AuthorController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Spec 01-upsert-role: insert 201, update and archive 200 (data.roleId), delete 204 with no body.</summary>
    [HttpPost("role/upsert")]
    public async Task<IActionResult> UpsertRole([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var shapeError = JsonBody.TryRead(body, RoleBody.Fields, out var b, uniqueKeys: true);
        if ((NoQuery() ?? shapeError) is { } message)
            return Result.Failure(Error.Validation("Role.Shape", message)).ToFailure(this);

        var result = await dispatcher.SendAsync(new UpsertRoleCommand(
            VerifiedCaller.Of(this), b.String("id"), b.String("name").Value, b.String("description").Value, b.String("rowStatus")),
            cancellationToken);

        return result.IsSuccess ? this.ToUpsert(result.Value.Action, "Role", new { roleId = result.Value.RoleId }) : result.ToFailure(this);
    }

    /// <summary>
    /// Spec 02-upsert-role-batch: 200 "Batch Upsert Role Success" for every committed batch, data.roles holding each
    /// item's index, roleId and action (INSERT, UPDATE, DELETE, ARCHIVE) in request order.
    /// </summary>
    [HttpPost("role/upsert/batch")]
    public async Task<IActionResult> UpsertRoleBatch([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var shapeError = RoleBatchBody.TryRead(body, out var items);
        if ((NoQuery() ?? shapeError) is { } message)
            return Result.Failure(Error.Validation("RoleBatch.Shape", message)).ToFailure(this);

        var roles = items.Select(b => new RoleBatchItem(b.String("id"), b.String("name").Value, b.String("description").Value, b.String("rowStatus"))).ToList();
        var result = await dispatcher.SendAsync(new UpsertRoleBatchCommand(VerifiedCaller.Of(this), roles), cancellationToken);
        if (result.IsFailure) return result.ToFailure(this);

        var data = new { roles = result.Value.Roles.Select(r => new { index = r.Index, roleId = r.Written.RoleId, action = r.Written.Action.ToString().ToUpperInvariant() }) };
        return this.ToSuccess(StatusCodes.Status200OK, "Ok", "Batch Upsert Role Success", data);
    }

    private static readonly HashSet<string> RoleQueryKeys = ["id", "pageIndex", "pageSize", "search", "rowStatus"];

    /// <summary>Spec 03-inquiry-role: one page of non-DELETED roles, sorted by name then id; platform administrators only.</summary>
    [HttpGet("role")]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        if (QueryParameters.TryRead(Request, RoleQueryKeys, out var q) is { } shapeError)
            return Result.Failure(Error.Validation("Role.Request", shapeError)).ToFailure(this);

        var result = await dispatcher.SendAsync(new InquiryRoleQuery(
            VerifiedCaller.Of(this), q.GetValueOrDefault("id"), q.GetValueOrDefault("pageIndex"), q.GetValueOrDefault("pageSize"),
            q.GetValueOrDefault("search"), q.GetValueOrDefault("rowStatus")), cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Inquiry Role Success", result.Value)
            : result.ToFailure(this);
    }

    /// <summary>The author upserts take no query parameter: every key is unknown.</summary>
    private string? NoQuery() =>
        Request.Query.Count == 0 ? null : ValidationMessages.Combine(Request.Query.Keys.Select(ValidationMessages.UnknownField));
}
