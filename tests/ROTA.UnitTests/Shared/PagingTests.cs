using FluentAssertions;
using ROTA.Shared;

namespace ROTA.UnitTests.Shared;

/// <summary>
/// The page offset must not overflow. Every paged query used to compute <c>(page - 1) * pageSize</c>
/// in int arithmetic; at the shipped leaderboard PageSize of 200 the product wraps negative from page
/// 10,737,420 on, and PostgreSQL rejects a negative OFFSET. That turned a query-string integer into a
/// 500 on /api/leaderboards, /api/guilds and /api/admin/emails — reproduced live before the fix.
/// </summary>
public class PagingTests
{
    [Theory]
    [InlineData(1, 200, 0)]
    [InlineData(2, 200, 200)]
    [InlineData(3, 50, 100)]
    public void Offset_IsTheOrdinaryProduct_ForOrdinaryPages(int page, int pageSize, int expected)
        => Paging.Offset(page, pageSize).Should().Be(expected);

    [Theory]
    [InlineData(10_737_420, 200)]   // the first page that overflowed at the shipped page size
    [InlineData(20_000_000, 200)]
    [InlineData(int.MaxValue, 200)]
    [InlineData(int.MaxValue, 1)]
    public void Offset_NeverGoesNegative_HoweverLargeThePage(int page, int pageSize)
        => Paging.Offset(page, pageSize).Should().BeGreaterThanOrEqualTo(0,
            "a negative OFFSET is a PostgreSQL error, so an out-of-range page must return an empty "
            + "page rather than a 500");

    [Fact]
    public void Offset_SaturatesRatherThanWrapping()
        => Paging.Offset(int.MaxValue, 200).Should().Be(int.MaxValue);

    [Theory]
    [InlineData(0, 200)]
    [InlineData(-1, 200)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    public void Offset_IsZero_ForNonsenseInput(int page, int pageSize)
        => Paging.Offset(page, pageSize).Should().Be(0);
}
