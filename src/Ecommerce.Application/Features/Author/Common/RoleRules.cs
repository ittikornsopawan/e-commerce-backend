using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>The input rules of one role (specs 01 and 02): the <see cref="MasterRules"/> with the role code.</summary>
public static class RoleRules
{
    public const string NameFormat = "Role name must be capital letters, digits and _, starting with a letter, at most 64 characters.";
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a role.";

    private static readonly MasterCode Code = new("name", RoleCode.IsValid, NameFormat, NoDeletedInsert);

    /// <summary>Every broken rule of one role, in request field order; empty when the role is valid.</summary>
    public static List<string> Messages(Optional<string?> id, string? name, string? description, Optional<string?> rowStatus) =>
        MasterRules.Messages(Code, id, name, [new MasterText("description", description, 256)], rowStatus);
}
