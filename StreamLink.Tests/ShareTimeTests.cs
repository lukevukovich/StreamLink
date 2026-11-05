using StreamLink.Services;

namespace StreamLink.Tests;

public sealed class ShareTimeTests
{
    [Theory]
    [InlineData(1, 0, "1h remaining")]
    [InlineData(23, 58, "23h 58m remaining")]
    [InlineData(24, 0, "1 day remaining")]
    [InlineData(25, 0, "1 day 1h remaining")]
    [InlineData(48, 0, "2 days remaining")]
    public void Remaining_UsesConciseDayAndHourLabels(int hours, int minutes, string expected)
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        var result = ShareTime.Remaining(now.AddHours(hours).AddMinutes(minutes), now);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Remaining_CanOmitTheSuffixForCompactTags()
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        var result = ShareTime.Remaining(now.AddHours(5).AddMinutes(12), now, includeSuffix: false);

        Assert.Equal("5h 12m", result);
    }

    [Fact]
    public void Remaining_FormatsTimeBeforeDateAndIncludesYearWhenNeeded()
    {
        var value = new DateTime(2026, 11, 5, 15, 15, 0, DateTimeKind.Utc);

        Assert.Matches(@"^3:15 PM, Nov 5(, 2026)? UTC$", ShareTime.Utc(value));
    }
}
