using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>The input rules of one scope (specs 07 and 08): the <see cref="MasterRules"/> with the code in <c>scope</c>, name at most 64, description at most 256.</summary>
public static class ScopeRules
{
    public const string CodeFormat = "Scope code must be capital letters, digits and _, starting with a letter, at most 64 characters.";
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a scope.";

    private static readonly MasterCode Code = new("scope", UpperCode.IsValid, CodeFormat, NoDeletedInsert);

    /// <summary>Every broken rule of one scope, in request field order; empty when the scope is valid.</summary>
    public static List<string> Messages(Optional<string?> id, string? scope, string? name, string? description, Optional<string?> rowStatus) =>
        MasterRules.Messages(Code, id, scope, [new MasterText("name", name, 64), new MasterText("description", description, 256)], rowStatus);
}
