using Ecommerce.Application.Features.Author.Common;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.UpsertPermission;

/// <summary>
/// Spec 04 input rules, all before the JWT and SQL: the <see cref="MasterRules"/> with the permission code, then each scope
/// link in input order: id a UUID, scopeId a required UUID, rowStatus ACTIVE or DELETED (never DELETED on an insert), no
/// repeated link id and no scope repeated among the links that are not DELETED. Link messages start with <c>scopes[i]</c>.
/// </summary>
public sealed class UpsertPermissionCommandValidator : IValidator<UpsertPermissionCommand>
{
    public const string NameFormat =
        "Permission name must be lower-case letters, digits, _ and ., with at least two parts separated by a dot, at most 64 characters.";
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a permission.";
    public const string NoDeletedLinkInsert = "Row status must not be DELETED when inserting a scope.";

    private static readonly MasterCode Code = new(PermissionCode.IsValid, NameFormat, NoDeletedInsert);
    private static readonly string[] LinkStatuses = [MasterLifecycle.Active, MasterLifecycle.Deleted];

    public IReadOnlyList<Error> Validate(UpsertPermissionCommand r)
    {
        var messages = MasterRules.Messages(Code, r.Id, r.Name, r.Description, r.RowStatus);
        messages.AddRange(LinkMessages(r.Scopes));
        return messages.Count == 0 ? [] : [Error.Validation("Permission.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }

    private static IEnumerable<string> LinkMessages(IReadOnlyList<ScopeLinkInput> links)
    {
        var firstIds = new Dictionary<Guid, int>();
        var firstScopes = new Dictionary<Guid, int>();
        for (var i = 0; i < links.Count; i++)
        {
            var item = ItemMessages(links[i], i, firstIds, firstScopes);
            if (item.Count > 0) yield return At($"scopes[{i}]", Combine(item));
        }
    }

    private static List<string> ItemMessages(ScopeLinkInput link, int index, Dictionary<Guid, int> firstIds, Dictionary<Guid, int> firstScopes)
    {
        var messages = new List<string>();
        var hasId = Guid.TryParse(link.Id.Value, out var id);
        if (link.Id.IsSet && !hasId) messages.Add(Uuid("id"));

        var hasScope = Guid.TryParse(link.ScopeId, out var scopeId);
        if (string.IsNullOrWhiteSpace(link.ScopeId)) messages.Add(Required("scopeId"));
        else if (!hasScope) messages.Add(Uuid("scopeId"));

        var deleted = AddStatusMessages(link, messages);
        if (hasId && !firstIds.TryAdd(id, index)) messages.Add(DuplicatedWith("id", $"scopes[{firstIds[id]}]"));
        if (hasScope && !deleted && !firstScopes.TryAdd(scopeId, index)) messages.Add(DuplicatedWith("scopeId", $"scopes[{firstScopes[scopeId]}]"));
        return messages;
    }

    /// <summary>Adds the rowStatus messages; true when the link is requested DELETED.</summary>
    private static bool AddStatusMessages(ScopeLinkInput link, List<string> messages)
    {
        if (!link.RowStatus.IsSet) return false;
        if (!LinkStatuses.Contains(link.RowStatus.Value)) messages.Add(OneOf("rowStatus", LinkStatuses));
        else if (!link.Id.IsSet && link.RowStatus.Value == MasterLifecycle.Deleted) messages.Add(NoDeletedLinkInsert);
        return link.RowStatus.Value == MasterLifecycle.Deleted;
    }
}
