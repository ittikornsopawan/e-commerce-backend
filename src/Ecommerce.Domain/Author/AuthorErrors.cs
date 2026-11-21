using Ecommerce.Shared.Results;

namespace Ecommerce.Domain.Author;

/// <summary>The author specs' errors, each with its spec status, bizCode and text.</summary>
public static class AuthorErrors
{
    // Spec 01-upsert-role.
    public static readonly Error ManageRolesForbidden =
        Error.Of("Author.ManageRolesForbidden", "You do not have permission to manage roles.", ErrorType.Forbidden, "E10009");

    public static readonly Error RoleNotFound = Error.Of("Author.RoleNotFound", "Role not found.", ErrorType.NotFound, "E10004");

    public static readonly Error RoleNameTaken = Error.Of("Author.RoleNameTaken", "Role name already exists.", ErrorType.Conflict, "E10003");

    public static readonly Error RoleInUse =
        Error.Of("Author.RoleInUse", "Role is assigned to users or has permissions.", ErrorType.Conflict, "E10006");

    // Spec 03-inquiry-role.
    public static readonly Error ReadRolesForbidden =
        Error.Of("Author.ReadRolesForbidden", "You do not have permission to read roles.", ErrorType.Forbidden, "E10009");

    public static readonly Error SystemRoleLocked =
        Error.Of("Author.SystemRoleLocked", "System role cannot be changed.", ErrorType.Conflict, "E10006");

    // Spec 04-upsert-permission.
    public static readonly Error ManagePermissionsForbidden =
        Error.Of("Author.ManagePermissionsForbidden", "You do not have permission to manage permissions.", ErrorType.Forbidden, "E10009");

    public static readonly Error PermissionNotFound = Error.Of("Author.PermissionNotFound", "Permission not found.", ErrorType.NotFound, "E10004");

    public static readonly Error PermissionScopeNotFound =
        Error.Of("Author.PermissionScopeNotFound", "Permission scope not found.", ErrorType.NotFound, "E10004");

    /// <summary>An ACTIVE link needs an ACTIVE scope master: a missing reference, so E10005.</summary>
    public static readonly Error ScopeNotFound = Error.Of("Author.ScopeNotFound", "Scope not found.", ErrorType.NotFound, "E10005");

    public static readonly Error ScopeIdChanged = Error.Validation("Author.ScopeIdChanged", "Scope Id cannot be changed.");

    public static readonly Error PermissionNameTaken =
        Error.Of("Author.PermissionNameTaken", "Permission name already exists.", ErrorType.Conflict, "E10003");

    public static readonly Error PermissionScopeTaken =
        Error.Of("Author.PermissionScopeTaken", "Scope already exists in the permission.", ErrorType.Conflict, "E10003");

    public static readonly Error PermissionInUse =
        Error.Of("Author.PermissionInUse", "Permission is used by roles or users.", ErrorType.Conflict, "E10006");

    public static readonly Error PermissionScopeInUse =
        Error.Of("Author.PermissionScopeInUse", "Scope is used by roles or users.", ErrorType.Conflict, "E10006");

    /// <summary>Q24: delete the remaining scope definitions before the permission.</summary>
    public static readonly Error PermissionScopesRemain =
        Error.Of("Author.PermissionScopesRemain", "Permission cannot be deleted while scope definitions remain.", ErrorType.Conflict, "E10006");

    /// <summary>Q22: a granted permission keeps at least one scope definition.</summary>
    public static readonly Error PermissionWouldBeUnscoped =
        Error.Of("Author.PermissionWouldBeUnscoped", "Permission cannot become unscoped while assigned to roles or users.", ErrorType.Conflict, "E10006");

    // Spec 06-inquiry-permission.
    public static readonly Error ReadPermissionsForbidden =
        Error.Of("Author.ReadPermissionsForbidden", "You do not have permission to read permissions.", ErrorType.Forbidden, "E10009");

    // Spec 07-upsert-scope. "Scope not found." of a missing master id is E10004 here (the target), E10005 in spec 04 (a reference).
    public static readonly Error ManageScopesForbidden =
        Error.Of("Author.ManageScopesForbidden", "You do not have permission to manage scopes.", ErrorType.Forbidden, "E10009");

    public static readonly Error ScopeTargetNotFound = Error.Of("Author.ScopeTargetNotFound", "Scope not found.", ErrorType.NotFound, "E10004");

    public static readonly Error ScopeCodeTaken = Error.Of("Author.ScopeCodeTaken", "Scope code already exists.", ErrorType.Conflict, "E10003");

    public static readonly Error ScopeInUse = Error.Of("Author.ScopeInUse", "Scope is used by permissions.", ErrorType.Conflict, "E10006");

    // Spec 09-inquiry-scope.
    public static readonly Error ReadScopesForbidden =
        Error.Of("Author.ReadScopesForbidden", "You do not have permission to read scopes.", ErrorType.Forbidden, "E10009");

    // Spec 10-upsert-user-role.
    public static readonly Error AssignRolesForbidden =
        Error.Of("Author.AssignRolesForbidden", "You do not have permission to assign roles.", ErrorType.Forbidden, "E10009");

    public static readonly Error UserRoleNotFound = Error.Of("Author.UserRoleNotFound", "User role not found.", ErrorType.NotFound, "E10004");

    /// <summary>The target user of an assignment does not exist (any status): a missing reference, so E10005.</summary>
    public static readonly Error TargetUserNotFound = Error.Of("Author.TargetUserNotFound", "User not found.", ErrorType.NotFound, "E10005");

    public static readonly Error UserOrRoleNotActive =
        Error.Of("Author.UserOrRoleNotActive", "User or role not found or not ACTIVE.", ErrorType.NotFound, "E10005");

    public static readonly Error UserRoleChanged = Error.Validation("Author.UserRoleChanged", "User Id and Role Id cannot be changed.");

    public static readonly Error UserRoleTaken = Error.Of("Author.UserRoleTaken", "The user already has this role.", ErrorType.Conflict, "E10003");

    public static readonly Error LastAdministrator =
        Error.Of("Author.LastAdministrator", "The platform must keep at least one administrator.", ErrorType.Conflict, "E10006");
}
