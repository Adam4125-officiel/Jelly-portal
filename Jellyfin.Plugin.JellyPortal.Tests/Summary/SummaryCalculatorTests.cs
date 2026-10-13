using Jellyfin.Plugin.JellyPortal.Summary;
using static Jellyfin.Plugin.JellyPortal.Tests.Summary.Builders;

namespace Jellyfin.Plugin.JellyPortal.Tests.Summary;

public class SummaryCalculatorTests
{
    // ---- the priority table, over every combination of conditions ----------------------------------

    /// <summary>The six independent things that can be true at once.</summary>
    [Flags]
    public enum Cond
    {
        None = 0,
        ServiceDown = 1,
        WindowOngoing = 2,
        ManualMaintenance = 4,
        ServiceDegraded = 8,
        OpenIncident = 16,
        SoonInWindow = 32,
    }

    public static IEnumerable<object[]> AllCombinations() => Enumerable.Range(0, 64).Select(n => new object[] { (Cond)n });

    /// <summary>
    /// The owner's table, written down independently of the implementation: down; then maintenance in
    /// progress (a window, or a service set to maintenance by hand); then degraded, where an open
    /// incident counts as at least degraded; then maintenance soon; then ok.
    /// </summary>
    private static string Expected(Cond c) =>
        c.HasFlag(Cond.ServiceDown) ? "down"
        : c.HasFlag(Cond.WindowOngoing) || c.HasFlag(Cond.ManualMaintenance) ? "maintenance_ongoing"
        : c.HasFlag(Cond.ServiceDegraded) || c.HasFlag(Cond.OpenIncident) ? "degraded"
        : c.HasFlag(Cond.SoonInWindow) ? "maintenance_soon"
        : "ok";

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void EveryCombinationOfConditionsGivesTheStateOfTheTable(Cond c)
    {
        var services = new List<PortalService> { Svc("Healthy") };
        if (c.HasFlag(Cond.ServiceDown)) { services.Add(Svc("Down one", ServiceStatus.Down)); }
        if (c.HasFlag(Cond.ManualMaintenance)) { services.Add(Svc("By hand", ServiceStatus.Maintenance)); }
        if (c.HasFlag(Cond.ServiceDegraded)) { services.Add(Svc("Degraded one", ServiceStatus.Degraded)); }

        var maintenance = new List<PortalMaintenance>();
        if (c.HasFlag(Cond.WindowOngoing)) { maintenance.Add(Active(TimeSpan.FromHours(1), "Healthy")); }
        if (c.HasFlag(Cond.SoonInWindow)) { maintenance.Add(Upcoming(TimeSpan.FromHours(2), "Healthy")); }

        var incidents = c.HasFlag(Cond.OpenIncident) ? new[] { Incident("Hand-made incident") } : [];

        Assert.Equal(Expected(c), Calc(Snapshot(services, incidents, maintenance)).State);
    }

    // ---- what is not a problem -----------------------------------------------------------------------

    [Fact]
    public void SlowAndUnrecognisedStatusesAreFine()
    {
        var dto = Calc(Snapshot([Svc("A", ServiceStatus.Slow), Svc("B", ServiceStatus.Unknown)]));
        Assert.Equal("ok", dto.State);
        Assert.Equal(["slow", "unknown"], dto.Services.Select(s => s.Status));
    }

    [Fact]
    public void NothingAtAllIsOk()
    {
        Assert.Equal("ok", Calc(Snapshot(services: [])).State);
    }

    [Fact]
    public void AnnouncementsNeverChangeTheIcon()
    {
        var critical = new PortalAnnouncement("Outage soon", "text", "critical", true);
        var dto = Calc(Snapshot(announcements: [critical]));
        Assert.Equal("ok", dto.State);
        Assert.Single(dto.Announcements);
        Assert.Equal("critical", dto.Announcements[0].Type);
        Assert.True(dto.Announcements[0].Pinned);
    }

    // ---- the "coming up" window ----------------------------------------------------------------------

    [Fact]
    public void AMaintenanceInsideTheWindowGivesTheClockBadge()
    {
        Assert.Equal("maintenance_soon", Calc(Snapshot(maintenance: [Upcoming(TimeSpan.FromHours(23), "Jellyfin")])).State);
    }

    [Fact]
    public void TheWindowBoundaryIsInclusive()
    {
        var edge = Snapshot(maintenance: [Upcoming(TimeSpan.FromHours(24), "Jellyfin")]);
        Assert.Equal("maintenance_soon", Calc(edge).State);
        var past = Snapshot(maintenance: [Upcoming(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1), "Jellyfin")]);
        Assert.Equal("ok", Calc(past).State);
    }

    [Fact]
    public void TheWindowIsTheConfiguredOneAndAppliesToEveryService()
    {
        var snapshot = Snapshot(
            [Svc("Jellyfin"), Svc("NAS")],
            maintenance: [Upcoming(TimeSpan.FromHours(30), "NAS")]);
        Assert.Equal("ok", Calc(snapshot).State);
        var wide = new SummaryOptions { SoonWindow = TimeSpan.FromHours(48) };
        Assert.Equal("maintenance_soon", Calc(snapshot, wide).State);
    }

    [Fact]
    public void APriorityServiceDoesNotWidenTheWindow()
    {
        // Priority only puts a maintenance forward in the panel; it never makes the badge appear earlier.
        var snapshot = Snapshot(maintenance: [Upcoming(TimeSpan.FromDays(3), "Jellyfin")]);
        var options = new SummaryOptions { PriorityServices = ["jellyfin"] };
        var dto = Calc(snapshot, options);
        Assert.Equal("ok", dto.State);
        Assert.True(dto.Maintenance.Upcoming[0].Priority);
        Assert.False(dto.Maintenance.Upcoming[0].InWindow);
    }

    // ---- "in progress" ---------------------------------------------------------------------------------

    [Fact]
    public void AWindowWhoseTimeHasComeCountsAsInProgressEvenIfThePortalHasNotFlippedItYet()
    {
        // The portal's flag only changes on its next check cycle, so right after the start it still says upcoming.
        var lagging = new PortalMaintenance("Late flag", "", false, Now.AddMinutes(-2), Now.AddMinutes(58), ["Jellyfin"]);
        var dto = Calc(Snapshot(maintenance: [lagging]));
        Assert.Equal("maintenance_ongoing", dto.State);
        Assert.Single(dto.Maintenance.Ongoing);
        Assert.Empty(dto.Maintenance.Upcoming);
    }

    [Fact]
    public void AWindowTheClockTurnsIntoInProgressBetweenTwoReadings()
    {
        var snapshot = Snapshot(maintenance: [Upcoming(TimeSpan.FromMinutes(5), "Jellyfin")]);
        Assert.Equal("maintenance_soon", Calc(snapshot).State);
        Assert.Equal("maintenance_ongoing", Calc(snapshot, asOf: Now.AddMinutes(6)).State);
    }

    [Fact]
    public void AWindowThatRanPastItsEndButIsStillFlaggedActiveIsStillInProgress()
    {
        var overrun = new PortalMaintenance("Overrun", "", true, Now.AddHours(-3), Now.AddMinutes(-10), ["Jellyfin"]);
        Assert.Equal("maintenance_ongoing", Calc(Snapshot(maintenance: [overrun])).State);
    }

    [Fact]
    public void AFinishedWindowThePortalHasNotClearedYetIsNotShownAnywhere()
    {
        var over = new PortalMaintenance("Over", "", false, Now.AddHours(-3), Now.AddHours(-2), ["Jellyfin"]);
        var dto = Calc(Snapshot(maintenance: [over]));
        Assert.Equal("ok", dto.State);
        Assert.Empty(dto.Maintenance.Ongoing);
        Assert.Empty(dto.Maintenance.Upcoming);
    }

    [Fact]
    public void AServiceSetToMaintenanceByHandCountsEvenWithoutAWindow()
    {
        var dto = Calc(Snapshot([Svc("NAS", ServiceStatus.Maintenance)]));
        Assert.Equal("maintenance_ongoing", dto.State);
        Assert.Empty(dto.Maintenance.Ongoing);
        Assert.Equal("maintenance", dto.Services[0].Status);
    }

    // ---- services the admin excluded from the overall status -----------------------------------------

    [Fact]
    public void AWindowOnAServiceTheListLeavesOutDoesNotCount()
    {
        // The portal omits excluded services from the list but not from windows and incidents.
        var snapshot = Snapshot([Svc("Jellyfin")], maintenance: [Active(TimeSpan.FromHours(1), "Muted thing")]);
        var dto = Calc(snapshot);
        Assert.Equal("ok", dto.State);
        Assert.Single(dto.Maintenance.Ongoing); // still shown in the panel
    }

    [Fact]
    public void AWindowCoveringAnExcludedAndAnIncludedServiceDoesCount()
    {
        var snapshot = Snapshot([Svc("Jellyfin")], maintenance: [Active(TimeSpan.FromHours(1), "Muted thing", "jellyfin")]);
        Assert.Equal("maintenance_ongoing", Calc(snapshot).State);
    }

    [Fact]
    public void AnIncidentOnlyAboutAnExcludedServiceDoesNotMakeTheIconDegraded()
    {
        var snapshot = Snapshot([Svc("Jellyfin")], incidents: [Incident("Muted is down", "Muted thing")]);
        var dto = Calc(snapshot);
        Assert.Equal("ok", dto.State);
        Assert.Single(dto.Incidents);
    }

    [Fact]
    public void AnIncidentThatCoversNoServiceCounts()
    {
        Assert.Equal("degraded", Calc(Snapshot(incidents: [Incident("Network is flaky")])).State);
    }

    [Fact]
    public void WhenTheServiceListWasCutNothingIsAssumedExcluded()
    {
        // 150 services counted, 100 listed: a name missing from the list proves nothing.
        var listed = Enumerable.Range(0, 100).Select(i => Svc($"S{i}")).ToList();
        var snapshot = Snapshot(listed, maintenance: [Active(TimeSpan.FromHours(1), "Not in the first 100")], total: 150);
        Assert.Equal("maintenance_ongoing", Calc(snapshot).State);
    }

    // ---- priority services ---------------------------------------------------------------------------

    [Fact]
    public void PriorityMatchesNamesIgnoringCase()
    {
        var options = new SummaryOptions { PriorityServices = ["JELLYFIN", "seerr"] };
        var snapshot = Snapshot(
            [Svc("Jellyfin"), Svc("Seerr"), Svc("NAS")],
            incidents: [Incident("a", "NAS"), Incident("b", "NAS", "seerr")]);
        var dto = Calc(snapshot, options);
        Assert.Equal([true, true, false], dto.Services.Select(s => s.Priority));
        Assert.Equal([false, true], dto.Incidents.Select(i => i.Priority));
    }

    [Fact]
    public void PriorityMaintenancesComeFirstThenTheSoonest()
    {
        var options = new SummaryOptions { PriorityServices = ["Jellyfin"] };
        var snapshot = Snapshot(
            [Svc("Jellyfin"), Svc("NAS")],
            maintenance:
            [
                Upcoming(TimeSpan.FromHours(1), "NAS"),
                Upcoming(TimeSpan.FromHours(30), "Jellyfin"),
                Upcoming(TimeSpan.FromHours(3), "NAS"),
                Active(TimeSpan.FromHours(2), "NAS"),
                Active(TimeSpan.FromHours(5), "Jellyfin"),
            ]);
        var dto = Calc(snapshot, options);
        Assert.Equal([true, false], dto.Maintenance.Ongoing.Select(m => m.Priority));
        Assert.Equal([true, false, false], dto.Maintenance.Upcoming.Select(m => m.Priority));
        Assert.Equal(
            ["2026-10-15T02:00:00Z", "2026-10-13T21:00:00Z", "2026-10-13T23:00:00Z"],
            dto.Maintenance.Upcoming.Select(m => m.StartsAt));
    }

    [Fact]
    public void OnlyTheNextTenUpcomingMaintenancesAreListed()
    {
        var many = Enumerable.Range(1, 15).Select(i => Upcoming(TimeSpan.FromHours(i), "Jellyfin")).ToList();
        var dto = Calc(Snapshot(maintenance: many));
        Assert.Equal(SummaryCalculator.MaxUpcoming, dto.Maintenance.Upcoming.Count);
        Assert.Equal("2026-10-13T21:00:00Z", dto.Maintenance.Upcoming[0].StartsAt);
    }

    // ---- the content of the panel ---------------------------------------------------------------------

    [Fact]
    public void TheDtoCarriesEverythingThePanelNeeds()
    {
        var snapshot = Snapshot(
            [Svc("Jellyfin", ServiceStatus.Down)],
            [new PortalIncident("Jellyfin is unreachable", "identified", Now.AddMinutes(-20), ["Jellyfin"])],
            [Active(TimeSpan.FromMinutes(45), "Jellyfin")],
            [new PortalAnnouncement("Movie night", "Friday at 8pm", "info", false)]);
        var dto = Calc(snapshot);
        Assert.Equal(1, dto.Schema);
        Assert.Equal("down", dto.State);
        Assert.Null(dto.Reason);
        Assert.Equal("2026-10-13T20:00:00Z", dto.Now);
        Assert.Equal("Home", dto.Portal.Name);
        Assert.Equal("https://portal.example", dto.Portal.Url);
        Assert.Equal("2026-10-13T19:40:00Z", dto.Incidents[0].Since);
        Assert.Equal("identified", dto.Incidents[0].Status);
        Assert.Equal("2026-10-13T20:45:00Z", dto.Maintenance.Ongoing[0].EndsAt);
        Assert.Equal("details", dto.Maintenance.Ongoing[0].Description);
        Assert.Equal("Movie night", dto.Announcements[0].Title);
    }

    [Fact]
    public void ATimeTheCalculatorIsGivenInAnotherOffsetIsWrittenInUtc()
    {
        var plus2 = new PortalMaintenance("t", "", false, new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.FromHours(2)), null, ["Jellyfin"]);
        var dto = Calc(Snapshot(maintenance: [plus2]));
        Assert.Equal("2026-10-14T20:00:00Z", dto.Maintenance.Upcoming[0].StartsAt);
        Assert.Null(dto.Maintenance.Upcoming[0].EndsAt);
    }

    // ---- unknown ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(SummaryFailure.NotConfigured, "not_configured")]
    [InlineData(SummaryFailure.DeviceApiDisabled, "device_api_disabled")]
    [InlineData(SummaryFailure.BadApiKey, "bad_api_key")]
    [InlineData(SummaryFailure.Unreachable, "unreachable")]
    [InlineData(SummaryFailure.Timeout, "timeout")]
    [InlineData(SummaryFailure.BadResponse, "bad_response")]
    [InlineData(SummaryFailure.PortalOutdated, "portal_outdated")]
    public void AnUnreadablePortalIsUnknownWithItsReasonAndNoData(SummaryFailure failure, string code)
    {
        var dto = SummaryCalculator.Unavailable(failure, "https://portal.example", Now);
        Assert.Equal("unknown", dto.State);
        Assert.Equal(code, dto.Reason);
        Assert.Empty(dto.Services);
        Assert.Empty(dto.Incidents);
        Assert.Empty(dto.Maintenance.Ongoing);
        Assert.Empty(dto.Maintenance.Upcoming);
        Assert.Empty(dto.Announcements);
        Assert.Equal("https://portal.example", dto.Portal.Url);
        Assert.Null(dto.Portal.FetchedAt);
    }

    [Fact]
    public void EveryFailureHasItsOwnCode()
    {
        var codes = Enum.GetValues<SummaryFailure>().Select(f => f.Name()).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.Equal(7, codes.Count);
    }

    [Fact]
    public void EveryStateHasItsOwnWireName()
    {
        Assert.Equal(
            ["down", "maintenance_ongoing", "degraded", "maintenance_soon", "ok", "unknown"],
            Enum.GetValues<SummaryState>().Select(s => s.Name()));
    }
}
