using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertRoleBatch;

/// <summary>
/// Spec 02 item rules in request order, all before the JWT and SQL: each role's <see cref="RoleRules"/>, then a
/// duplicated id (compared as UUIDs) and a duplicated name among the items that are not DELETED (a deleted item does not
/// reserve its name). Each message starts with <c>roles[i]</c>; the whole batch is refused.
/// </summary>
public sealed class UpsertRoleBatchCommandValidator : IValidator<UpsertRoleBatchCommand>
{
    public IReadOnlyList<Error> Validate(UpsertRoleBatchCommand r)
    {
        var firstIds = new Dictionary<Guid, int>();
        var firstNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var messages = new List<string>();

        for (var i = 0; i < r.Roles.Count; i++)
        {
            var item = ItemMessages(r.Roles[i], i, firstIds, firstNames);
            if (item.Count > 0) messages.Add(At(Position(i), Combine(item)));
        }

        return messages.Count == 0 ? [] : [Error.Validation("RoleBatch.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }

    public static string Position(int index) => $"roles[{index}]";

    private static List<string> ItemMessages(RoleBatchItem item, int index, Dictionary<Guid, int> firstIds, Dictionary<string, int> firstNames)
    {
        var messages = RoleRules.Messages(item.Id, item.Name, item.Description, item.RowStatus);

        if (item.Id.IsSet && Guid.TryParse(item.Id.Value, out var id) && !firstIds.TryAdd(id, index))
            messages.Add($"Id is duplicated with {Position(firstIds[id])}.");

        var deleted = item.RowStatus.IsSet && item.RowStatus.Value == MasterLifecycle.Deleted;
        if (!deleted && item.Name is { } name && RoleCode.IsValid(name) && !firstNames.TryAdd(name, index))
            messages.Add($"Name is duplicated with {Position(firstNames[name])}.");

        return messages;
    }
}
