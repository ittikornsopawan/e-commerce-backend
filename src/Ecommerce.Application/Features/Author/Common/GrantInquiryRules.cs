using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>
/// The query rules of a grant inquiry (user role 11, role permission 14, user permission 16), all before the JWT and SQL:
/// id and the two key filters UUIDs, then the <see cref="MasterInquiryRules"/> paging and rowStatus.
/// </summary>
public static class GrantInquiryRules
{
    /// <summary>Every broken rule in query field order; empty when the query is valid.</summary>
    public static List<string> Messages(
        string? id, (string Field, string? Value) first, (string Field, string? Value) second, string? pageIndex, string? pageSize, string? rowStatus)
    {
        var messages = new[] { ("id", id), first, second }
            .Where(f => f.Item2 is not null && !Guid.TryParse(f.Item2, out _))
            .Select(f => Uuid(f.Item1))
            .ToList();
        messages.AddRange(MasterInquiryRules.Messages(null, pageIndex, pageSize, rowStatus));
        return messages;
    }

    /// <summary>A validated optional UUID filter.</summary>
    public static Guid? ParseOrNull(string? id) => id is null ? null : Guid.Parse(id);
}
