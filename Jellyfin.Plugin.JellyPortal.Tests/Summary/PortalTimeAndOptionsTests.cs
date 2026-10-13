using Jellyfin.Plugin.JellyPortal.Summary;
using static Jellyfin.Plugin.JellyPortal.Tests.Summary.Builders;

namespace Jellyfin.Plugin.JellyPortal.Tests.Summary;

public class PortalTimeAndOptionsTests
{
    [Theory]
    [InlineData("2026-10-13T20:42:10Z", "2026-10-13T20:42:10Z")]
    [InlineData("2026-10-13T20:42:10z", "2026-10-13T20:42:10Z")]
    [InlineData("2026-10-13T22:42:10+02:00", "2026-10-13T20:42:10Z")]
    [InlineData("2026-10-13T20:42", "2026-10-13T20:42:00Z")]   // a datetime-local value: UTC by the portal's convention
    [InlineData("2026-10-13T20:42:10", "2026-10-13T20:42:10Z")]
    public void PortalTimesAreReadAsUtc(string text, string expected)
    {
        Assert.Equal(expected, PortalTime.Format(PortalTime.Parse(text)!.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("2026-13-45T99:99")]
    public void BlankOrUnreadableTimesAreNull(string? text)
    {
        Assert.Null(PortalTime.Parse(text));
    }

    [Fact]
    public void TheMachineLocalTimeZoneNeverLeaksIntoParsing()
    {
        var previous = TimeZoneInfo.Local;
        // Parsing must not depend on the zone of the machine: a naive value is UTC everywhere.
        Assert.Equal(TimeSpan.Zero, PortalTime.Parse("2026-07-01T12:00")!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, PortalTime.Parse("2026-01-01T12:00")!.Value.Offset);
        Assert.Same(previous, TimeZoneInfo.Local);
    }

    [Fact]
    public void ThePortalClockMovesOnByTheTimeElapsedSinceItWasRead()
    {
        var readAt = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero); // this machine's (wrong) clock
        var estimate = PortalTime.EstimatePortalNow(Now, readAt, readAt.AddSeconds(25));
        Assert.Equal(Now.AddSeconds(25), estimate);
    }

    [Fact]
    public void ThisMachinesAbsoluteClockIsNotTrusted()
    {
        // The portal says it is 20:00; this machine thinks it is 2030. Only the elapsed time counts.
        var readAt = new DateTimeOffset(2030, 6, 1, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(Now, PortalTime.EstimatePortalNow(Now, readAt, readAt));
    }

    [Fact]
    public void AClockThatStepsBackwardsNeverMovesThePortalClockBack()
    {
        var readAt = Now.AddHours(1);
        Assert.Equal(Now, PortalTime.EstimatePortalNow(Now, readAt, readAt.AddMinutes(-5)));
    }

    [Fact]
    public void PriorityNamesAreOneLineEachTrimmedAndDeduplicatedIgnoringCase()
    {
        var names = SummaryOptions.ParsePriorityNames("  Jellyfin \r\n\r\nSeerr\nJELLYFIN\n   \nNAS (4K)  ");
        Assert.Equal(["Jellyfin", "Seerr", "NAS (4K)"], names);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n \r\n")]
    public void NoPriorityNamesIsAnEmptyList(string? text)
    {
        Assert.Empty(SummaryOptions.ParsePriorityNames(text));
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(24, 24)]
    [InlineData(720, 720)]
    [InlineData(100000, 720)]
    public void TheWindowIsClampedBetweenOneHourAndThirtyDays(int hours, int expected)
    {
        Assert.Equal(TimeSpan.FromHours(expected), SummaryOptions.WindowFromHours(hours));
    }

    [Fact]
    public void TheDefaultWindowIsTwentyFourHours()
    {
        Assert.Equal(TimeSpan.FromHours(24), new SummaryOptions().SoonWindow);
    }
}
