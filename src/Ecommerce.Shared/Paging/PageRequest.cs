using System.Globalization;

namespace Ecommerce.Shared.Paging;

/// <summary>Why a pageIndex/pageSize pair was refused.</summary>
public enum PageProblem
{
    None,
    /// <summary>Not a positive signed 64-bit integer, or pageSize above the maximum.</summary>
    OutOfRange,
    /// <summary>(pageIndex - 1) * pageSize does not fit a nonnegative BIGINT.</summary>
    OffsetOverflow
}

/// <summary>
/// Checked offset pagination (spec 10): positive 64-bit pageIndex/pageSize, defaults 1/10 only when omitted, and an
/// offset that fits a nonnegative BIGINT. Shared because the Application validates it and Infrastructure binds it.
/// </summary>
public sealed record PageRequest(long PageIndex, long PageSize)
{
    public const long DefaultPageIndex = 1;
    public const long DefaultPageSize = 10;

    public long Offset => checked((PageIndex - 1) * PageSize);

    /// <summary>An omitted value takes its default; an explicit value must be digits only, never silently defaulted.</summary>
    public static PageProblem TryParse(string? pageIndex, string? pageSize, long maxPageSize, out PageRequest page)
    {
        page = new PageRequest(DefaultPageIndex, DefaultPageSize);
        if (!TryPositive(pageIndex, DefaultPageIndex, out var index) || !TryPositive(pageSize, DefaultPageSize, out var size) || size > maxPageSize)
            return PageProblem.OutOfRange;

        page = new PageRequest(index, size);
        try
        {
            _ = page.Offset;
            return PageProblem.None;
        }
        catch (OverflowException)
        {
            return PageProblem.OffsetOverflow;
        }
    }

    /// <summary>0 when there are no items, otherwise ceiling(totalItems / pageSize).</summary>
    public long TotalPages(long totalItems) => totalItems == 0 ? 0 : (totalItems - 1) / PageSize + 1;

    /// <summary>The row count this page must hold: min(pageSize, max(0, totalItems - offset)).</summary>
    public long ExpectedRows(long totalItems) => Math.Min(PageSize, Math.Max(0, totalItems - Offset));

    private static bool TryPositive(string? value, long whenOmitted, out long number)
    {
        number = whenOmitted;
        return value is null || (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0);
    }
}

/// <summary>One page and its totals; the five data keys of a paged inquiry.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, long PageIndex, long PageSize, long TotalItems, long TotalPages);
