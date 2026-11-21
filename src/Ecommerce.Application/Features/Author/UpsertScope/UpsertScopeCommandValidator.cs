using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertScope;

/// <summary>Spec 07 input rules, all before the JWT and SQL: the <see cref="MasterRules"/> with the scope code in <c>scope</c>, name at most 64 and description at most 256.</summary>
public sealed class UpsertScopeCommandValidator : IValidator<UpsertScopeCommand>
{
    public const string CodeFormat = "Scope code must be capital letters, digits and _, starting with a letter, at most 64 characters.";
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a scope.";

    private static readonly MasterCode Code = new("scope", UpperCode.IsValid, CodeFormat, NoDeletedInsert);

    public IReadOnlyList<Error> Validate(UpsertScopeCommand r)
    {
        var messages = MasterRules.Messages(Code, r.Id, r.Scope, [new MasterText("name", r.Name, 64), new MasterText("description", r.Description, 256)], r.RowStatus);
        return messages.Count == 0 ? [] : [Error.Validation("Scope.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
