using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertScope;

/// <summary>Spec 07 input rules (<see cref="ScopeRules"/>), all before the JWT and SQL.</summary>
public sealed class UpsertScopeCommandValidator : IValidator<UpsertScopeCommand>
{
    public IReadOnlyList<Error> Validate(UpsertScopeCommand r)
    {
        var messages = ScopeRules.Messages(r.Id, r.Scope, r.Name, r.Description, r.RowStatus);
        return messages.Count == 0 ? [] : [Error.Validation("Scope.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
