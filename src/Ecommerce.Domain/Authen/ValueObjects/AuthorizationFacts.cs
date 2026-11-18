namespace Ecommerce.Domain.Authen.ValueObjects;

// The rows the login reads for authorization (spec 02-login "Get active ..." queries), already filtered by those queries.

/// <summary>An ACTIVE role of an ACTIVE assignment.</summary>
public sealed record RoleFact(Guid RoleId, string Name);

/// <summary>An ACTIVE t_role_permission row of an active role.</summary>
public sealed record RolePermissionFact(Guid RolePermissionId, Guid PermissionId);

/// <summary>An ACTIVE t_role_permission_scope row; <c>PermissionScopeId</c> is t_permission_scope.id.</summary>
public sealed record RoleScopeLinkFact(Guid RolePermissionId, Guid PermissionScopeId);

/// <summary>An ACTIVE t_user_permission row; a NULL scope is an unscoped grant.</summary>
public sealed record DirectPermissionFact(Guid PermissionId, Guid? PermissionScopeId);

/// <summary>A non-DELETED t_permission_scope row.</summary>
public sealed record PermissionScopeDefinition(Guid Id, Guid PermissionId, Guid ScopeId, string RowStatus);

/// <summary>An ACTIVE m_permission row.</summary>
public sealed record PermissionFact(Guid PermissionId, string Name);

/// <summary>An ACTIVE m_scope row; <c>Code</c> is m_scope.scope (for example OWN).</summary>
public sealed record ScopeFact(Guid ScopeId, string Code);

public sealed record AuthorizationFacts(
    IReadOnlyList<RoleFact> Roles,
    IReadOnlyList<RolePermissionFact> RolePermissions,
    IReadOnlyList<RoleScopeLinkFact> RoleScopeLinks,
    IReadOnlyList<DirectPermissionFact> DirectPermissions,
    IReadOnlyList<PermissionScopeDefinition> Definitions,
    IReadOnlyList<PermissionFact> ActivePermissions);

/// <summary>The JWT claims: sorted unique names (spec 02-login claim note 6).</summary>
public sealed record ResolvedClaims(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, IReadOnlyList<string> Scopes);
