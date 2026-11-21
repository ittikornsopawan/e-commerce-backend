using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertRole;

/// <summary>Spec 01 input rules (<see cref="RoleRules"/>), all before the JWT check and SQL.</summary>
public sealed class UpsertRoleCommandValidator : IValidator<UpsertRoleCommand>
{
    public IReadOnlyList<Error> Validate(UpsertRoleCommand r)
    {
        var messages = RoleRules.Messages(r.Id, r.Name, r.Description, r.RowStatus);
        return messages.Count == 0 ? [] : [Error.Validation("Role.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
