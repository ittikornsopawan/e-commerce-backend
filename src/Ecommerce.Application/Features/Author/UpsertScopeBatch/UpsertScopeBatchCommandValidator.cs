using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertScopeBatch;

/// <summary>
/// Spec 08 item rules in request order, all before the JWT and SQL: each scope's <see cref="ScopeRules"/>, then a
/// duplicated id (compared as UUIDs) and a duplicated code among the items that are not DELETED. Each message starts with
/// <c>scopes[i]</c>; the whole batch is refused.
/// </summary>
public sealed class UpsertScopeBatchCommandValidator : IValidator<UpsertScopeBatchCommand>
{
    public IReadOnlyList<Error> Validate(UpsertScopeBatchCommand r)
    {
        var firstIds = new Dictionary<Guid, int>();
        var firstCodes = new Dictionary<string, int>(StringComparer.Ordinal);
        var messages = new List<string>();

        for (var i = 0; i < r.Scopes.Count; i++)
        {
            var item = ItemMessages(r.Scopes[i], i, firstIds, firstCodes);
            if (item.Count > 0) messages.Add(At(Position(i), Combine(item)));
        }

        return messages.Count == 0 ? [] : [Error.Validation("ScopeBatch.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }

    public static string Position(int index) => $"scopes[{index}]";

    private static List<string> ItemMessages(ScopeBatchItem item, int index, Dictionary<Guid, int> firstIds, Dictionary<string, int> firstCodes)
    {
        var messages = ScopeRules.Messages(item.Id, item.Scope, item.Name, item.Description, item.RowStatus);

        if (item.Id.IsSet && Guid.TryParse(item.Id.Value, out var id) && !firstIds.TryAdd(id, index))
            messages.Add(DuplicatedWith("id", Position(firstIds[id])));

        var deleted = item.RowStatus.IsSet && item.RowStatus.Value == MasterLifecycle.Deleted;
        if (!deleted && item.Scope is { } code && UpperCode.IsValid(code) && !firstCodes.TryAdd(code, index))
            messages.Add(DuplicatedWith("scope", Position(firstCodes[code])));

        return messages;
    }
}
