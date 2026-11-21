using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertPermission;

/// <summary>Spec 04 input rules (<see cref="PermissionRules"/>), all before the JWT check and SQL.</summary>
public sealed class UpsertPermissionCommandValidator : IValidator<UpsertPermissionCommand>
{
    public IReadOnlyList<Error> Validate(UpsertPermissionCommand r)
    {
        var messages = PermissionRules.Messages(r.Id, r.Name, r.Description, r.RowStatus, r.Scopes);
        return messages.Count == 0 ? [] : [Error.Validation("Permission.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }
}
