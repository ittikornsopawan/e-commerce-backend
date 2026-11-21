namespace Ecommerce.Infrastructure.Persistence.Ef;

/// <summary>LIMIT/OFFSET of a paged inquiry from the checked 64-bit values of <c>PageRequest</c>.</summary>
public static class PageQueryExtensions
{
    /// <summary>
    /// EF Skip/Take take int, while the specs allow a BIGINT offset. An offset past int.MaxValue is past every row of
    /// these tables, so it is capped there: the page is still empty, as the specs require.
    /// </summary>
    public static IQueryable<T> PageOf<T>(this IQueryable<T> ordered, long offset, long limit) =>
        ordered.Skip((int)Math.Min(offset, int.MaxValue)).Take((int)limit);
}
