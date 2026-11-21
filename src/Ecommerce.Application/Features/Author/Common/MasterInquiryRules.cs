using Ecommerce.Application.Common;
using Ecommerce.Domain.Author.Domain;
using Ecommerce.Domain.Author.Repositories;
using Ecommerce.Shared.Paging;
using static Ecommerce.Shared.Validation.ValidationMessages;

namespace Ecommerce.Application.Features.Author.Common;

/// <summary>
/// The query rules of an author master inquiry (role 03, permission 06, scope 09), all before the JWT and SQL: id a UUID,
/// checked pagination (page size at most 100), rowStatus any status but DELETED. The values stay raw strings so an explicit
/// empty or invalid value is refused instead of defaulted.
/// </summary>
public static class MasterInquiryRules
{
    public const long MaxPageSize = 100;

    /// <summary>Every broken rule, in query field order; empty when the query is valid.</summary>
    public static List<string> Messages(string? id, string? pageIndex, string? pageSize, string? rowStatus)
    {
        var messages = new List<string>();
        if (id is not null && !Guid.TryParse(id, out _)) messages.Add(Uuid("id"));
        if (Page(PageRequest.TryParse(pageIndex, pageSize, MaxPageSize, out _), MaxPageSize) is { } page) messages.Add(page);
        if (rowStatus is not null && !MasterLifecycle.Visible.Contains(rowStatus)) messages.Add(RowStatusFilter(MasterLifecycle.Visible));
        return messages;
    }

    /// <summary>The validated page; call only after <see cref="Messages"/> returned none.</summary>
    public static PageRequest PageOf(string? pageIndex, string? pageSize)
    {
        PageRequest.TryParse(pageIndex, pageSize, MaxPageSize, out var page);
        return page;
    }

    /// <summary>The validated filters; search becomes <c>%search%</c>.</summary>
    public static MasterFilter Filter(string? id, string? search, string? rowStatus) =>
        new(id is null ? null : Guid.Parse(id), PagedRead.SearchPattern(search), rowStatus);
}
