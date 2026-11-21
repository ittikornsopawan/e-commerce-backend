using Ecommerce.Shared.Paging;

namespace Ecommerce.Application.Common;

/// <summary>
/// The read of a paged inquiry (authen 10 and the author inquiries): count first, fetch the page only when the count is
/// positive, then require the page to hold the rows the count of the same snapshot expects.
/// </summary>
public static class PagedRead
{
    /// <summary>
    /// Runs inside the caller's read-only snapshot. A row count that disagrees with the count is a defect, so it throws:
    /// the transaction rolls back and the API answers 500, never a partial page.
    /// </summary>
    public static async Task<Page<TItem>> ReadAsync<TRow, TItem>(
        PageRequest page, Func<Task<long>> count, Func<long, long, Task<IReadOnlyList<TRow>>> list, Func<TRow, TItem> map)
    {
        var total = await count();
        var rows = total == 0 ? [] : await list(page.Offset, page.PageSize);
        if (rows.Count != page.ExpectedRows(total))
            throw new InvalidOperationException($"The page holds {rows.Count} rows; the count of the same snapshot expects {page.ExpectedRows(total)}.");

        return new Page<TItem>(rows.Select(map).ToList(), page.PageIndex, page.PageSize, total, page.TotalPages(total));
    }

    /// <summary>The bound ILIKE pattern of a search filter: <c>%search%</c>, keeping the client's % and _; null when omitted.</summary>
    public static string? SearchPattern(string? search) => search is null ? null : $"%{search}%";
}
