using PlannerWidget.Core.Attendance;

namespace PlannerWidget.Core.Tests;

public class WorkTimeTests
{
    [Theory]
    [InlineData(7, 29, 7)]
    [InlineData(7, 30, 8)]
    [InlineData(7, 0, 7)]
    [InlineData(23, 45, 0)]
    public void RoundToNearestHour(int hour, int minute, int expectedHour)
    {
        var rounded = WorkTime.RoundToNearestHour(new DateTime(2026, 3, 3, hour, minute, 10));
        Assert.Equal(expectedHour, rounded.Hour);
        Assert.Equal(0, rounded.Minute);
        Assert.Equal(0, rounded.Second);
    }

    [Fact]
    public void ComputeHours_uses_rounded_endpoints()
    {
        var start = new DateTime(2026, 3, 3, 8, 40, 0); // -> 9:00
        var end = new DateTime(2026, 3, 3, 16, 10, 0);  // -> 16:00
        Assert.Equal(7, WorkTime.ComputeHours(start, end));
    }

    [Fact]
    public void ComputeHours_never_negative()
    {
        var start = new DateTime(2026, 3, 3, 9, 0, 0);
        Assert.Equal(0, WorkTime.ComputeHours(start, start.AddMinutes(-90)));
    }

    [Theory]
    [InlineData("7", 7)]
    [InlineData("7,5", 7.5)]
    [InlineData("7.5 óra", 7.5)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseHours(string? raw, double expected) => Assert.Equal(expected, WorkTime.ParseHours(raw));

    [Fact]
    public void Formatting()
    {
        Assert.Equal("9:00", WorkTime.FormatClock(new DateTime(2026, 1, 1, 9, 0, 0)));
        Assert.Equal("148", WorkTime.FormatHours(148));
        Assert.Equal("7.5", WorkTime.FormatHours(7.5));
        Assert.Equal("45 perc", WorkTime.FormatDuration(TimeSpan.FromMinutes(45)));
        Assert.Equal("2 ó 05 p", WorkTime.FormatDuration(TimeSpan.FromMinutes(125)));
    }
}

public class AttendancePdfLocatorTests
{
    private const string March = @"\\fs2.example\MO\jelenlétik\2026. március\Minta Név_Jelenléti_március.pdf";

    [Fact]
    public void DetectPeriod_reads_year_and_month()
    {
        Assert.Equal((2026, 3), AttendancePdfLocator.DetectPeriod(March));
    }

    [Fact]
    public void SuggestPathFor_replaces_month_everywhere()
    {
        var october = AttendancePdfLocator.SuggestPathFor(March, new DateOnly(2026, 10, 1));
        Assert.Equal(@"\\fs2.example\MO\jelenlétik\2026. október\Minta Név_Jelenléti_október.pdf", october);
    }

    [Fact]
    public void SuggestPathFor_rolls_year()
    {
        var january = AttendancePdfLocator.SuggestPathFor(March, new DateOnly(2027, 1, 5));
        Assert.Equal(@"\\fs2.example\MO\jelenlétik\2027. január\Minta Név_Jelenléti_január.pdf", january);
    }

    [Fact]
    public void IsForMonth()
    {
        Assert.True(AttendancePdfLocator.IsForMonth(March, new DateOnly(2026, 3, 31)));
        Assert.False(AttendancePdfLocator.IsForMonth(March, new DateOnly(2026, 4, 1)));
    }

    [Fact]
    public void Path_without_month_gives_no_suggestion()
    {
        Assert.Null(AttendancePdfLocator.SuggestPathFor(@"C:\docs\jelenleti.pdf", new DateOnly(2026, 10, 1)));
    }
}
