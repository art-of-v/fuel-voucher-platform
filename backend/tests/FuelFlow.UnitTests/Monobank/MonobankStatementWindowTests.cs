using FluentAssertions;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;

namespace FuelFlow.UnitTests.Monobank;

/// <summary>
/// The acquirer refuses a statement request wider than <see cref="MonobankStatementWindow.MaxRangeDays"/>
/// days, so anything above that has to be cut up before it is sent.
/// </summary>
/// <remarks>
/// The property worth pinning is the tiling. A slice that overlaps its neighbour costs nothing - the
/// upsert absorbs the repeat - whereas a gap costs a period out of every pass, silently, and the act
/// built from it is simply short.
/// </remarks>
public class MonobankStatementWindowTests
{
    private static readonly DateTime To = new(2026, 10, 13, 9, 17, 2, DateTimeKind.Utc);

    [Fact]
    public void A_Window_Inside_The_Limit_Is_One_Slice()
    {
        var slices = MonobankStatementWindow.Split(To.AddDays(-30), To);

        slices.Should().ContainSingle();
        slices[0].FromUtc.Should().Be(To.AddDays(-30));
        slices[0].ToUtc.Should().Be(To);
    }

    // The production shape: a 35-day window is wider than the acquirer accepts, and asking for it in
    // one request is what left the table empty.
    [Fact]
    public void A_Window_Wider_Than_The_Limit_Is_Cut_Into_Slices_That_Fit()
    {
        var slices = MonobankStatementWindow.Split(To.AddDays(-35), To);

        slices.Should().HaveCount(2);
        slices.Should().OnlyContain(s => s.ToUtc - s.FromUtc <= TimeSpan.FromDays(MonobankStatementWindow.MaxRangeDays));
        slices.Should().OnlyContain(s => s.ToUtc - s.FromUtc > TimeSpan.Zero);
    }

    [Fact]
    public void The_Slices_Tile_The_Window_With_No_Gap_And_No_Overlap()
    {
        var from = To.AddDays(-35);

        var slices = MonobankStatementWindow.Split(from, To);

        slices[0].FromUtc.Should().Be(from);
        slices[^1].ToUtc.Should().Be(To);
        for (var i = 1; i < slices.Count; i++)
        {
            // Exactly equal, not merely adjacent: one bound must be the other's bound, or a row
            // stamped between them lands in neither.
            slices[i].FromUtc.Should().Be(slices[i - 1].ToUtc);
        }
    }

    // Oldest first, so the slice that failed is the one whose absence is least surprising when
    // reading the job log top to bottom.
    [Fact]
    public void The_Slices_Are_Ordered_Oldest_First()
    {
        var slices = MonobankStatementWindow.Split(To.AddDays(-70), To);

        slices.Should().HaveCount(3);
        slices.Select(s => s.FromUtc).Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_Window_Covering_No_Period_Produces_No_Slices(int days)
    {
        var from = To.AddDays(-days);

        MonobankStatementWindow.Split(from, To).Should().BeEmpty();
    }
}