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

    public static readonly Error SystemRoleLocked =
        Error.Of("Author.SystemRoleLocked", "System role cannot be changed.", ErrorType.Conflict, "E10006");
}
