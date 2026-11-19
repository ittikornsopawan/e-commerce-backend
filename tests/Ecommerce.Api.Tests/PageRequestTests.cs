using Ecommerce.Shared.Paging;

namespace Ecommerce.Api.Tests;

// The checked pagination of spec 10 (TC-ADDRESSQ-005 and 006), at its boundaries.
public sealed class PageRequestTests
{
    [Theory]
    [InlineData(null, null, 1L, 10L)]
    [InlineData("3", "100", 3L, 100L)]
    [InlineData("007", "1", 7L, 1L)]
    public void Omitted_values_default_and_digits_parse(string? index, string? size, long expectedIndex, long expectedSize)
    {
        Assert.Equal(PageProblem.None, PageRequest.TryParse(index, size, 100, out var page));
        Assert.Equal(new PageRequest(expectedIndex, expectedSize), page);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData(" 1", null)]
    [InlineData("+1", null)]
    [InlineData("-1", null)]
    [InlineData("9223372036854775808", null)]
    [InlineData(null, "101")]
    [InlineData(null, "0")]
    public void Invalid_or_explicit_empty_values_are_out_of_range(string? index, string? size) =>
        Assert.Equal(PageProblem.OutOfRange, PageRequest.TryParse(index, size, 100, out _));

    [Fact]
    public void An_offset_beyond_bigint_is_refused_and_the_largest_fitting_one_is_not()
    {
        Assert.Equal(PageProblem.OffsetOverflow, PageRequest.TryParse("9223372036854775807", "2", 100, out _));
        Assert.Equal(PageProblem.None, PageRequest.TryParse("9223372036854775807", "1", 100, out var page));
        Assert.Equal(long.MaxValue - 1, page.Offset);
    }

    [Theory]
    [InlineData(0L, 10L, 0L)]
    [InlineData(1L, 10L, 1L)]
    [InlineData(10L, 10L, 1L)]
    [InlineData(11L, 10L, 2L)]
    [InlineData(long.MaxValue, 1L, long.MaxValue)]
    public void Total_pages_is_the_ceiling_and_zero_without_items(long totalItems, long pageSize, long expected) =>
        Assert.Equal(expected, new PageRequest(1, pageSize).TotalPages(totalItems));

    [Theory]
    [InlineData(1L, 2L, 3L, 2L)]
    [InlineData(2L, 2L, 3L, 1L)]
    [InlineData(3L, 2L, 3L, 0L)]
    [InlineData(1L, 10L, 0L, 0L)]
    public void Expected_rows_follow_the_count(long index, long size, long totalItems, long expected) =>
        Assert.Equal(expected, new PageRequest(index, size).ExpectedRows(totalItems));
}
