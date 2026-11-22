using Ecommerce.Application.Features.Authen.Common;
using Ecommerce.Shared.Cqrs;

namespace Ecommerce.Application.Features.Author.CheckPermission;

/// <summary>GET v1/author/check (spec 17): raw query values (null when omitted); the caller is null when the JWT did not verify.</summary>
public sealed record CheckPermissionQuery(Caller? Caller, string? Permission, string? Scope) : IQuery<CheckPermissionResult>;

/// <summary>data of spec 17: the checked claims as requested (Scope null when not asked) and the answer.</summary>
public sealed record CheckPermissionResult(string Permission, string? Scope, bool Allowed);
