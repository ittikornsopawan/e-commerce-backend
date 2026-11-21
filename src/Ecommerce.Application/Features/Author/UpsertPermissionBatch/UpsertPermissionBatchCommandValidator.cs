using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertPermissionBatch;

/// <summary>
/// Spec 05 item rules in request order, all before the JWT and SQL: each permission's <see cref="PermissionRules"/> (its
/// links included), then a duplicated id (compared as UUIDs) and a duplicated name among the items that are not DELETED.
/// Each message starts with <c>permissions[i]</c>; the whole batch is refused.
/// </summary>
public sealed class UpsertPermissionBatchCommandValidator : IValidator<UpsertPermissionBatchCommand>
{
    public IReadOnlyList<Error> Validate(UpsertPermissionBatchCommand r)
    {
        var firstIds = new Dictionary<Guid, int>();
        var firstNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var messages = new List<string>();

        for (var i = 0; i < r.Permissions.Count; i++)
        {
            var item = ItemMessages(r.Permissions[i], i, firstIds, firstNames);
            if (item.Count > 0) messages.Add(At(Position(i), Combine(item)));
        }

        return messages.Count == 0 ? [] : [Error.Validation("PermissionBatch.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }

    public static string Position(int index) => $"permissions[{index}]";

    private static List<string> ItemMessages(PermissionBatchItem item, int index, Dictionary<Guid, int> firstIds, Dictionary<string, int> firstNames)
    {
        var messages = PermissionRules.Messages(item.Id, item.Name, item.Description, item.RowStatus, item.Scopes);

        if (item.Id.IsSet && Guid.TryParse(item.Id.Value, out var id) && !firstIds.TryAdd(id, index))
            messages.Add(DuplicatedWith("id", Position(firstIds[id])));

        var deleted = item.RowStatus.IsSet && item.RowStatus.Value == MasterLifecycle.Deleted;
        if (!deleted && item.Name is { } name && PermissionCode.IsValid(name) && !firstNames.TryAdd(name, index))
            messages.Add(DuplicatedWith("name", Position(firstNames[name])));

        return messages;
    }
}
