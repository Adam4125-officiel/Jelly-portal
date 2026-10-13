using System.Net;
using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;
using Jellyfin.Plugin.JellyPortal.Tests.Summary;
using static Jellyfin.Plugin.JellyPortal.Tests.Portal.Settings;

namespace Jellyfin.Plugin.JellyPortal.Tests.Portal;

/// <summary>
/// The plugin against REAL answers of the portal (captured from its own route, see Fixtures/portal-1.12.0).
/// Everything else in the suite reads payloads written from the contract; these are the ones that prove the
/// contract was understood.
/// </summary>
public class PortalContractTests
{
    private const string Folder = "Jellyfin.Plugin.JellyPortal.Tests/Fixtures/portal-1.12.0/";

    private static string Fixture(string name) => RepoFiles.Read(Folder + name);

    private static PortalSnapshot Full() => DevicePayloadParser.Parse(Fixture("full_scenario.json")).Snapshot!;

    // ---- the seeded scenario ------------------------------------------------------------------------------

    [Fact]
    public void TheRealAnswerIsParsedIntoWhatTheSeedContains()
    {
        var s = Full();
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 21, 23, 48, TimeSpan.Zero), s.PortalNow);
        Assert.Equal("Home Server", s.SiteName);
        Assert.Equal(11, s.ServicesTotal);               // 12 services, one muted
        Assert.Equal(11, s.Services.Count);
        Assert.DoesNotContain(s.Services, x => x.Name.Contains("muted", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            [ServiceStatus.Down, ServiceStatus.Degraded, ServiceStatus.Maintenance, ServiceStatus.Slow],
            s.Services.Take(4).Select(x => x.Status));
        Assert.Equal(2, s.Incidents.Count);
        Assert.Equal(3, s.Maintenance.Count);
        Assert.Equal(2, s.Announcements.Count);
        Assert.DoesNotContain(s.Announcements, a => a.Title.Contains("SCHEDULED", StringComparison.Ordinal));
    }

    [Fact]
    public void ANameThatContainsCommasIsOneNameInTheArrayNotSeveral()
    {
        const string name = "Nextcloud family photos, documents and archive";
        var s = Full();
        Assert.Contains(s.Services, x => x.Name == name);
        var incident = s.Incidents.Single(i => i.Title.StartsWith("Nextcloud and Seerr", StringComparison.Ordinal));
        Assert.Equal([name, "Seerr"], incident.Services);
        Assert.Contains(name, s.Maintenance.Single(m => m.Title == "Media disk replacement").Services);
    }

    [Fact]
    public void AnIncidentThatCoversNoServiceHasNoServices()
    {
        var wifi = Full().Incidents.Single(i => i.Title.StartsWith("Slow Wi-Fi", StringComparison.Ordinal));
        Assert.Empty(wifi.Services);
    }

    [Fact]
    public void TheWindowInProgressIsFlaggedAndCarriesItsDescription()
    {
        var router = Full().Maintenance.Single(m => m.Title == "Router firmware upgrade");
        Assert.True(router.MarkedActive);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 20, 53, 0, TimeSpan.Zero), router.Starts);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 22, 23, 0, TimeSpan.Zero), router.Ends);
        Assert.StartsWith("Updating to the latest firmware", router.Description, StringComparison.Ordinal);
        Assert.Equal(["Router admin"], router.Services);
    }

    [Fact]
    public void EveryNameInAnArrayIsAnExactNameFromTheServiceList()
    {
        // This is what lets the plugin match a priority service and tell a muted one from a listed one.
        var s = Full();
        var listed = s.Services.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var named = s.Incidents.SelectMany(i => i.Services).Concat(s.Maintenance.SelectMany(m => m.Services));
        Assert.All(named, n => Assert.Contains(n, listed));
    }

    [Fact]
    public void TheRealScenarioGivesTheExpectedIconAndPanel()
    {
        var options = new SummaryOptions { PriorityServices = ["jellyfin"] };
        var asOf = Full().PortalNow;
        var dto = SummaryCalculator.Calculate(Full(), asOf, options, "https://portal.example", asOf);

        Assert.Equal("down", dto.State);                                    // a service is down, which wins over everything
        Assert.Equal("Nextcloud family photos, documents and archive", dto.Services[0].Name);
        Assert.Equal(2, dto.Incidents.Count);
        var ongoing = Assert.Single(dto.Maintenance.Ongoing);
        Assert.Equal("Router firmware upgrade", ongoing.Title);
        Assert.Equal(2, dto.Maintenance.Upcoming.Count);
        Assert.Equal("Media disk replacement", dto.Maintenance.Upcoming[0].Title);   // priority first (Jellyfin)
        Assert.True(dto.Maintenance.Upcoming[0].Priority);
        Assert.True(dto.Maintenance.Upcoming[0].InWindow);                   // in 2 h, inside the 24 h window
        Assert.False(dto.Maintenance.Upcoming[1].InWindow);                  // in 3 days
        Assert.Equal(2, dto.Announcements.Count);
    }

    [Fact]
    public void WithTheDownServiceHealedTheRouterMaintenanceIsWhatShows()
    {
        var s = Full();
        var healed = s with { Services = s.Services.Select(x => x.Status == ServiceStatus.Down ? x with { Status = ServiceStatus.Operational } : x).ToList() };
        var dto = SummaryCalculator.Calculate(healed, healed.PortalNow, new SummaryOptions(), "", healed.PortalNow);
        Assert.Equal("maintenance_ongoing", dto.State);
    }

    // ---- an older portal -------------------------------------------------------------------------------------

    [Fact]
    public void TheAnswerOfAnOlderPortalIsRecognisedAsOutdated()
    {
        Assert.Equal(SummaryFailure.PortalOutdated, DevicePayloadParser.Parse(Fixture("default_scenario.json")).Failure);
    }

    // ---- the worst case --------------------------------------------------------------------------------------

    [Fact]
    public void TheWorstCaseTheCapsAllowIsStillWithinWhatTheClientReads()
    {
        var body = Fixture("full_worstcase.json");
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(body) < PortalClient.MaxBodyBytes);
        var s = DevicePayloadParser.Parse(body).Snapshot!;
        Assert.Equal(100, s.Services.Count);
        Assert.Equal(110, s.ServicesTotal);          // the list was cut, so nothing may be inferred from a missing name
        Assert.Equal(10, s.Incidents.Count);
        Assert.Equal(20, s.Maintenance.Count);
        Assert.Equal(10, s.Announcements.Count);
        Assert.All(s.Incidents, i => Assert.Equal(16, i.Services.Count));
    }

    [Fact]
    public void TheWorstCaseIsComputedAndSerialisedWithoutTroubleAndStaysBounded()
    {
        var s = DevicePayloadParser.Parse(Fixture("full_worstcase.json")).Snapshot!;
        var dto = SummaryCalculator.Calculate(s, s.PortalNow, new SummaryOptions(), "https://portal.example", s.PortalNow);
        var json = SummaryJson.Serialize(dto);
        Assert.True(json.Length < 400_000, $"{json.Length} characters is more than a browser should be sent");
        Assert.Equal(SummaryCalculator.MaxUpcoming, dto.Maintenance.Upcoming.Count);
    }

    [Fact]
    public async Task TheClientReadsTheWorstCaseThroughTheWholePath()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Fixture("full_worstcase.json"));
        using var client = Client(handler, new ManualTime(Builders.Now));
        var reading = await client.ReadAsync(Configured(), CancellationToken.None);
        Assert.NotNull(reading.Outcome.Snapshot);
    }

    [Fact]
    public async Task TheClientReadsTheRealAnswerAndTheOlderPortalAnswerAsExpected()
    {
        using var good = Client(new StubHandler(HttpStatusCode.OK, Fixture("full_scenario.json")), new ManualTime(Builders.Now));
        Assert.NotNull((await good.ReadAsync(Configured(), CancellationToken.None)).Outcome.Snapshot);
        using var old = Client(new StubHandler(HttpStatusCode.OK, Fixture("default_scenario.json")), new ManualTime(Builders.Now));
        Assert.Equal(SummaryFailure.PortalOutdated, (await old.ReadAsync(Configured(), CancellationToken.None)).Outcome.Failure);
    }
}
