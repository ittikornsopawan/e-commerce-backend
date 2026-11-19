using Ecommerce.Shared.Cqrs;
using Ecommerce.Shared.Results;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Authen.UpsertUserAddress;

/// <summary>
/// Spec 09 input rules, all before SQL: the six text fields mandatory and non-blank (also for update and delete), title
/// at most 128, id a UUID, rowStatus ACTIVE/INACTIVE/DELETED, Q21 (a default must be ACTIVE) and no DELETED insert.
/// </summary>
public sealed class UpsertUserAddressCommandValidator : IValidator<UpsertUserAddressCommand>
{
    public const string DefaultMustBeActive = "A default address must be ACTIVE.";
    public const string NoDeletedInsert = "Row status must not be DELETED when inserting a user address.";
    private static readonly string[] Statuses = ["ACTIVE", "INACTIVE", "DELETED"];

    public IReadOnlyList<Error> Validate(UpsertUserAddressCommand r)
    {
        var messages = new List<string>();
        AddFieldMessages(r, messages);
        AddLifecycleMessages(r, messages);
        return messages.Count == 0 ? [] : [Error.Validation("Address.Invalid", messages.Count == 1 ? messages[0] : Combine(messages))];
    }

    private static void AddFieldMessages(UpsertUserAddressCommand r, List<string> messages)
    {
        if (r.Id.IsSet && !Guid.TryParse(r.Id.Value, out _)) messages.Add(Uuid("id"));

        var missing = new[] { ("title", r.Title), ("address", r.Address), ("city", r.City), ("state", r.State), ("postalCode", r.PostalCode), ("country", r.Country) }
            .Where(f => string.IsNullOrWhiteSpace(f.Item2))
            .Select(f => f.Item1)
            .ToArray();
        if (missing.Length > 0) messages.Add(Required(missing));

        if (r.Title is { Length: > 128 }) messages.Add(MaxLength("title", 128));
        if (r.RowStatus.IsSet && !Statuses.Contains(r.RowStatus.Value)) messages.Add(OneOf("rowStatus", Statuses));
    }

    private static void AddLifecycleMessages(UpsertUserAddressCommand r, List<string> messages)
    {
        var status = r.RowStatus.IsSet ? r.RowStatus.Value : "ACTIVE";
        // Q21 (product owner, 2026-11-08): an explicit true with INACTIVE or DELETED is refused, even for an unknown id.
        if (r.IsDefault == true && status is "INACTIVE" or "DELETED") messages.Add(DefaultMustBeActive);
        if (!r.Id.IsSet && status == "DELETED") messages.Add(NoDeletedInsert);
    }
}
