using System.Text.Json;
using Ecommerce.Api.Contracts;
using Ecommerce.Api.Contracts.Author;
using Ecommerce.Api.Extensions;
using Ecommerce.Api.Security;
using Ecommerce.Application.Features.Author.InquiryPermission;
using Ecommerce.Application.Features.Author.InquiryRole;
using Ecommerce.Application.Features.Author.InquiryScope;
using Ecommerce.Application.Features.Author.InquiryUserRole;
using Ecommerce.Application.Features.Author.Me;
using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Application.Features.Author.UpsertPermission;
using Ecommerce.Application.Features.Author.UpsertPermissionBatch;
using Ecommerce.Application.Features.Author.UpsertRole;
using Ecommerce.Application.Features.Author.UpsertRoleBatch;
using Ecommerce.Application.Features.Author.UpsertScope;
using Ecommerce.Application.Features.Author.UpsertScopeBatch;
using Ecommerce.Application.Features.Author.UpsertUserRole;
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

    /// <summary>Spec 04-upsert-permission: insert 201, update and archive 200 (data.permissionId), delete 204 with no body.</summary>
    [HttpPost("permission/upsert")]
    public async Task<IActionResult> UpsertPermission([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var shapeError = PermissionBody.TryRead(body, out var b, out var scopes);
        if ((NoQuery() ?? shapeError) is { } message)
            return Result.Failure(Error.Validation("Permission.Shape", message)).ToFailure(this);

        var links = scopes.Select(s => new ScopeLinkInput(s.String("id"), s.String("scopeId").Value, s.String("rowStatus"))).ToList();
        var result = await dispatcher.SendAsync(new UpsertPermissionCommand(
            VerifiedCaller.Of(this), b.String("id"), b.String("name").Value, b.String("description").Value, b.String("rowStatus"), links),
            cancellationToken);

        return result.IsSuccess
            ? this.ToUpsert(result.Value.Action, "Permission", new { permissionId = result.Value.PermissionId })
            : result.ToFailure(this);
    }

    /// <summary>The query keys of the author master inquiries (03, 06, 09).</summary>
    private static readonly HashSet<string> MasterQueryKeys = ["id", "pageIndex", "pageSize", "search", "rowStatus"];

    /// <summary>Spec 03-inquiry-role: one page of non-DELETED roles, sorted by name then id; platform administrators only.</summary>
    [HttpGet("role")]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        if (QueryParameters.TryRead(Request, MasterQueryKeys, out var q) is { } shapeError)
            return Result.Failure(Error.Validation("Role.Request", shapeError)).ToFailure(this);

        var result = await dispatcher.SendAsync(new InquiryRoleQuery(
            VerifiedCaller.Of(this), q.GetValueOrDefault("id"), q.GetValueOrDefault("pageIndex"), q.GetValueOrDefault("pageSize"),
            q.GetValueOrDefault("search"), q.GetValueOrDefault("rowStatus")), cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Inquiry Role Success", result.Value)
            : result.ToFailure(this);
    }

    /// <summary>
    /// Spec 05-upsert-permission-batch: 200 "Batch Upsert Permission Success" for every committed batch, data.permissions
    /// holding each item's index, permissionId and action (INSERT, UPDATE, DELETE, ARCHIVE) in request order.
    /// </summary>
    [HttpPost("permission/upsert/batch")]
    public async Task<IActionResult> UpsertPermissionBatch([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var shapeError = PermissionBatchBody.TryRead(body, out var items);
        if ((NoQuery() ?? shapeError) is { } message)
            return Result.Failure(Error.Validation("PermissionBatch.Shape", message)).ToFailure(this);

        var permissions = items.Select(p => new PermissionBatchItem(
            p.Permission.String("id"), p.Permission.String("name").Value, p.Permission.String("description").Value, p.Permission.String("rowStatus"),
            p.Scopes.Select(s => new ScopeLinkInput(s.String("id"), s.String("scopeId").Value, s.String("rowStatus"))).ToList())).ToList();
        var result = await dispatcher.SendAsync(new UpsertPermissionBatchCommand(VerifiedCaller.Of(this), permissions), cancellationToken);
        if (result.IsFailure) return result.ToFailure(this);

        var data = new { permissions = result.Value.Permissions.Select(p => new { index = p.Index, permissionId = p.Written.PermissionId, action = p.Written.Action.ToString().ToUpperInvariant() }) };
        return this.ToSuccess(StatusCodes.Status200OK, "Ok", "Batch Upsert Permission Success", data);
    }

    /// <summary>Spec 06-inquiry-permission: one page of non-DELETED permissions with their scopes; platform administrators only.</summary>
    [HttpGet("permission")]
    public async Task<IActionResult> GetPermissions(CancellationToken cancellationToken)
    {
        if (QueryParameters.TryRead(Request, MasterQueryKeys, out var q) is { } shapeError)
            return Result.Failure(Error.Validation("Permission.Request", shapeError)).ToFailure(this);

        var result = await dispatcher.SendAsync(new InquiryPermissionQuery(
            VerifiedCaller.Of(this), q.GetValueOrDefault("id"), q.GetValueOrDefault("pageIndex"), q.GetValueOrDefault("pageSize"),
            q.GetValueOrDefault("search"), q.GetValueOrDefault("rowStatus")), cancellationToken);

        return result.IsSuccess
            ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Inquiry Permission Success", result.Value)
            : result.ToFailure(this);
    }

    /// <summary>Spec 07-upsert-scope: insert 201, update and archive 200 (data.scopeId), delete 204 with no body.</summary>
    [HttpPost("scope/upsert")]
    public async Task<IActionResult> UpsertScope([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var shapeError = JsonBody.TryRead(body, ScopeBody.Fields, out var b, uniqueKeys: true);
        if ((NoQuery() ?? shapeError) is { } message)
            return Result.Failure(Error.Validation("Scope.Shape", message)).ToFailure(this);

        var result = await dispatcher.SendAsync(new UpsertScopeCommand(
            VerifiedCaller.Of(this), b.String("id"), b.String("scope").Value, b.String("name").Value, b.String("description").Value, b.String("rowStatus")),
            cancellationToken);

        return result.IsSuccess ? this.ToUpsert(result.Value.Action, "Scope", new { scopeId = result.Value.ScopeId }) : result.ToFailure(this);
    }

    /// <summary>Spec 08-upsert-scope-batch: 200 "Batch Upsert Scope Success", data.scopes holding each item's index, scopeId and action.</summary>
    [HttpPost("scope/upsert/batch")]
    public async Task<IActionResult> UpsertScopeBatch([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var shapeError = ScopeBatchBody.TryRead(body, out var items);
        if ((NoQuery() ?? shapeError) is { } message)
            return Result.Failure(Error.Validation("ScopeBatch.Shape", message)).ToFailure(this);

        var scopes = items.Select(b => new ScopeBatchItem(b.String("id"), b.String("scope").Value, b.String("name").Value, b.String("description").Value, b.String("rowStatus"))).ToList();
        var result = await dispatcher.SendAsync(new UpsertScopeBatchCommand(VerifiedCaller.Of(this), scopes), cancellationToken);
        if (result.IsFailure) return result.ToFailure(this);

        var data = new { scopes = result.Value.Scopes.Select(s => new { index = s.Index, scopeId = s.Written.ScopeId, action = s.Written.Action.ToString().ToUpperInvariant() }) };
        return this.ToSuccess(StatusCodes.Status200OK, "Ok", "Batch Upsert Scope Success", data);
    }

    /// <summary>Spec 09-inquiry-scope: one page of non-DELETED scopes, sorted by code then id; platform administrators only.</summary>
    [HttpGet("scope")]
    public async Task<IActionResult> GetScopes(CancellationToken cancellationToken)
    {
        if (QueryParameters.TryRead(Request, MasterQueryKeys, out var q) is { } shapeError)
            return Result.Failure(Error.Validation("Scope.Request", shapeError)).ToFailure(this);

        var result = await dispatcher.SendAsync(new InquiryScopeQuery(
            VerifiedCaller.Of(this), q.GetValueOrDefault("id"), q.GetValueOrDefault("pageIndex"), q.GetValueOrDefault("pageSize"),
            q.GetValueOrDefault("search"), q.GetValueOrDefault("rowStatus")), cancellationToken);

        return result.IsSuccess ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Inquiry Scope Success", result.Value) : result.ToFailure(this);
    }

    /// <summary>Spec 10-upsert-user-role: insert 201, update and archive 200 (data.userRoleId), delete 204 with no body.</summary>
    [HttpPost("user/role/upsert")]
    public async Task<IActionResult> UpsertUserRole([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var shapeError = JsonBody.TryRead(body, UserRoleBody.Fields, out var b, uniqueKeys: true);
        if ((NoQuery() ?? shapeError) is { } message)
            return Result.Failure(Error.Validation("UserRole.Shape", message)).ToFailure(this);

        var result = await dispatcher.SendAsync(new UpsertUserRoleCommand(
            VerifiedCaller.Of(this), b.String("id"), b.String("userId").Value, b.String("roleId").Value, b.String("rowStatus")), cancellationToken);

        return result.IsSuccess ? this.ToUpsert(result.Value.Action, "User Role", new { userRoleId = result.Value.UserRoleId }) : result.ToFailure(this);
    }

    private static readonly HashSet<string> UserRoleQueryKeys = ["id", "userId", "roleId", "pageIndex", "pageSize", "rowStatus"];

    /// <summary>Spec 11-inquiry-user-role: one page of non-DELETED assignments; platform administrators only.</summary>
    [HttpGet("user/role")]
    public async Task<IActionResult> GetUserRoles(CancellationToken cancellationToken)
    {
        if (QueryParameters.TryRead(Request, UserRoleQueryKeys, out var q) is { } shapeError)
            return Result.Failure(Error.Validation("UserRole.Request", shapeError)).ToFailure(this);

        var result = await dispatcher.SendAsync(new InquiryUserRoleQuery(
            VerifiedCaller.Of(this), q.GetValueOrDefault("id"), q.GetValueOrDefault("userId"), q.GetValueOrDefault("roleId"),
            q.GetValueOrDefault("pageIndex"), q.GetValueOrDefault("pageSize"), q.GetValueOrDefault("rowStatus")), cancellationToken);

        return result.IsSuccess ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Inquiry User Role Success", result.Value) : result.ToFailure(this);
    }

    /// <summary>Spec 12-me: the caller and the calling token's roles; no query parameter and no body.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        if (QueryParameters.TryRead(Request, NoKeys, out _) is { } shapeError)
            return Result.Failure(Error.Validation("Me.Request", shapeError)).ToFailure(this);

        var result = await dispatcher.SendAsync(new MeQuery(VerifiedCaller.Of(this)), cancellationToken);
        return result.IsSuccess ? this.ToSuccess(StatusCodes.Status200OK, "Ok", "Inquiry Me Success", result.Value) : result.ToFailure(this);
    }

    private static readonly HashSet<string> NoKeys = [];

    /// <summary>The author upserts take no query parameter: every key is unknown.</summary>
    private string? NoQuery() =>
        Request.Query.Count == 0 ? null : ValidationMessages.Combine(Request.Query.Keys.Select(ValidationMessages.UnknownField));
}
