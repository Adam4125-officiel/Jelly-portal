using System.Text.Json.Nodes;
using Jellyfin.Plugin.JellyPortal.Summary;

namespace Jellyfin.Plugin.JellyPortal.Tests.Summary;

public class DevicePayloadParserTests
{
    /// <summary>
    /// What the portal answers to ?sections=services,incidents,maintenance,announcements&amp;services=all&amp;detail=full,
    /// per the contract in its device_api.py.
    /// </summary>
    internal const string Valid = """
    {"v":1,"now":"2026-10-13T20:00:00Z","site":"Home Server","overall":"down","detail":"full",
     "services":{"total":5,"operational":2,"slow":1,"degraded":1,"maintenance":0,"down":1,"items":[
        {"name":"Nextcloud","status":"down"},{"name":"Sonarr","status":"degraded"},
        {"name":"Radarr","status":"slow","ms":900},{"name":"Jellyfin","status":"operational","ms":45},
        {"name":"Seerr","status":"operational","ms":30}]},
     "incidents":{"open":2,"items":[
        {"title":"Nextcloud is unreachable","status":"investigating","since":"2026-10-13T19:40:12Z",
         "services":"Nextcloud","service_names":["Nextcloud"]},
        {"title":"Network is flaky","status":"monitoring","since":"2026-10-13T18:00:00Z",
         "services":"","service_names":[]}]},
     "maintenance":{"active":1,"upcoming":1,"items":[
        {"title":"Router firmware","state":"active","services":"Seerr","starts":"2026-10-13T19:30:00Z",
         "ends":"2026-10-13T20:30:00Z","service_names":["Seerr"],"description":"Brief outage"},
        {"title":"Disk replacement","state":"upcoming","services":"Jellyfin, Radarr","starts":"2026-10-14T22:00:00Z",
         "ends":"2026-10-15T01:00:00Z","service_names":["Jellyfin","Radarr"],"description":""}]},
     "announcements":{"count":1,"items":[
        {"title":"Movie night","text":"Friday at 8pm, bring snacks.","type":"info","pinned":true}]}}
    """;

    private static JsonObject Doc() => JsonNode.Parse(Valid)!.AsObject();

    private static ParseOutcome Parse(JsonObject doc) => DevicePayloadParser.Parse(doc.ToJsonString());

    private static PortalSnapshot Snapshot(string json) =>
        DevicePayloadParser.Parse(json).Snapshot ?? throw new Xunit.Sdk.XunitException("expected a snapshot");

    // ---- a good answer ----------------------------------------------------------------------------------

    [Fact]
    public void AValidAnswerBecomesASnapshot()
    {
        var s = Snapshot(Valid);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero), s.PortalNow);
        Assert.Equal("Home Server", s.SiteName);
        Assert.Equal(5, s.ServicesTotal);
        Assert.Equal(
            [("Nextcloud", ServiceStatus.Down), ("Sonarr", ServiceStatus.Degraded), ("Radarr", ServiceStatus.Slow),
             ("Jellyfin", ServiceStatus.Operational), ("Seerr", ServiceStatus.Operational)],
            s.Services.Select(x => (x.Name, x.Status)));
        Assert.Equal(2, s.Incidents.Count);
        Assert.Equal("Nextcloud is unreachable", s.Incidents[0].Title);
        Assert.Equal("investigating", s.Incidents[0].Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 19, 40, 12, TimeSpan.Zero), s.Incidents[0].Since);
        Assert.Equal(["Nextcloud"], s.Incidents[0].Services);
        Assert.Empty(s.Incidents[1].Services);
        Assert.Equal(2, s.Maintenance.Count);
        Assert.True(s.Maintenance[0].MarkedActive);
        Assert.False(s.Maintenance[1].MarkedActive);
        Assert.Equal(["Jellyfin", "Radarr"], s.Maintenance[1].Services);
        Assert.Equal("Brief outage", s.Maintenance[0].Description);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 22, 0, 0, TimeSpan.Zero), s.Maintenance[1].Starts);
        Assert.Equal(new PortalAnnouncement("Movie night", "Friday at 8pm, bring snacks.", "info", true), s.Announcements[0]);
    }

    [Fact]
    public void TheNameArraysAreUsedAndTheCutStringsAreNot()
    {
        // "services" on an incident or window is one string the portal cuts; only service_names is reliable.
        var doc = Doc();
        doc["maintenance"]!["items"]![1]!["services"] = "Jelly...";
        Assert.Equal(["Jellyfin", "Radarr"], Parse(doc).Snapshot!.Maintenance[1].Services);
    }

    [Fact]
    public void UnknownExtraKeysAreIgnored()
    {
        var doc = Doc();
        doc["future_key"] = 42;
        doc["services"]!["new"] = "x";
        doc["services"]!["items"]![0]!["extra"] = true;
        Assert.NotNull(Parse(doc).Snapshot);
    }

    [Fact]
    public void ANewServiceStatusIsKeptAsUnknownRatherThanRefused()
    {
        var doc = Doc();
        doc["services"]!["items"]![0]!["status"] = "on_fire";
        Assert.Equal(ServiceStatus.Unknown, Parse(doc).Snapshot!.Services[0].Status);
    }

    [Fact]
    public void AnOddItemIsSkippedAndTheRestIsKept()
    {
        var doc = Doc();
        var items = doc["services"]!["items"]!.AsArray();
        items.Add(JsonValue.Create("not an object"));
        items.Add(new JsonObject { ["status"] = "down" });           // no name
        items.Add(new JsonObject { ["name"] = "", ["status"] = "down" });
        Assert.Equal(5, Parse(doc).Snapshot!.Services.Count);
    }

    [Fact]
    public void MissingNameArraysAndUnreadableTimesAreTolerated()
    {
        var doc = Doc();
        var window = doc["maintenance"]!["items"]![1]!.AsObject();
        window.Remove("service_names");
        window["starts"] = "garbage";
        window["ends"] = "";
        var m = Parse(doc).Snapshot!.Maintenance[1];
        Assert.Empty(m.Services);
        Assert.Null(m.Starts);
        Assert.Null(m.Ends);
    }

    [Fact]
    public void TheTotalIsNeverBelowTheNumberOfItemsListed()
    {
        var doc = Doc();
        doc["services"]!["total"] = 2;
        Assert.Equal(5, Parse(doc).Snapshot!.ServicesTotal);
        doc["services"]!.AsObject().Remove("total");
        Assert.Equal(5, Parse(doc).Snapshot!.ServicesTotal);
    }

    [Fact]
    public void NonAsciiNamesSurvive()
    {
        var doc = Doc();
        doc["services"]!["items"]![0]!["name"] = "Café serveur \U0001F4A5";
        Assert.Equal("Café serveur \U0001F4A5", Parse(doc).Snapshot!.Services[0].Name);
    }

    // ---- answers that cannot be used --------------------------------------------------------------------

    [Fact]
    public void AnAnswerWithoutTheFullMarkerComesFromAPortalThatIgnoredDetailFull()
    {
        var doc = Doc();
        doc.Remove("detail");
        Assert.Equal(SummaryFailure.PortalOutdated, Parse(doc).Failure);
        doc["detail"] = "compact";
        Assert.Equal(SummaryFailure.PortalOutdated, Parse(doc).Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("{")]
    [InlineData("<html>502 Bad Gateway</html>")]
    public void SomethingThatIsNotTheSummaryIsABadResponse(string body)
    {
        Assert.Equal(SummaryFailure.BadResponse, DevicePayloadParser.Parse(body).Failure);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    public void AnotherSchemaVersionIsABadResponse(int version)
    {
        var doc = Doc();
        doc["v"] = version;
        Assert.Equal(SummaryFailure.BadResponse, Parse(doc).Failure);
    }

    [Fact]
    public void AMissingVersionIsABadResponse()
    {
        var doc = Doc();
        doc.Remove("v");
        Assert.Equal(SummaryFailure.BadResponse, Parse(doc).Failure);
    }

    [Theory]
    [InlineData("services")]
    [InlineData("incidents")]
    [InlineData("maintenance")]
    [InlineData("announcements")]
    public void AMissingSectionIsABadResponse(string section)
    {
        var doc = Doc();
        doc.Remove(section);
        Assert.Equal(SummaryFailure.BadResponse, Parse(doc).Failure);
    }

    [Theory]
    [InlineData("services")]
    [InlineData("incidents")]
    [InlineData("maintenance")]
    [InlineData("announcements")]
    public void ASectionThatIsNotAnObjectIsABadResponse(string section)
    {
        var doc = Doc();
        doc[section] = "oops";
        Assert.Equal(SummaryFailure.BadResponse, Parse(doc).Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    public void WithoutAReadableNowThePortalClockIsUnknownSoTheAnswerIsRefused(string? now)
    {
        var doc = Doc();
        doc["now"] = now;
        Assert.Equal(SummaryFailure.BadResponse, Parse(doc).Failure);
    }

    [Fact]
    public void AnEmptyButWellFormedPortalIsFine()
    {
        const string empty = """
        {"v":1,"now":"2026-10-13T20:00:00Z","site":"S","overall":"operational","detail":"full",
         "services":{"total":0,"items":[]},"incidents":{"open":0,"items":[]},
         "maintenance":{"active":0,"upcoming":0,"items":[]},"announcements":{"count":0,"items":[]}}
        """;
        var s = Snapshot(empty);
        Assert.Empty(s.Services);
        Assert.Empty(s.Incidents);
        Assert.Empty(s.Maintenance);
        Assert.Empty(s.Announcements);
    }

    // ---- end to end through the calculator --------------------------------------------------------------

    [Fact]
    public void ThePortalAnswerOfTheContractGivesTheExpectedPanel()
    {
        var dto = Builders.Calc(Snapshot(Valid));
        Assert.Equal("down", dto.State);                                   // Nextcloud is down
        Assert.Equal(["down", "degraded", "slow", "operational", "operational"], dto.Services.Select(s => s.Status));
        Assert.Equal(2, dto.Incidents.Count);
        Assert.Single(dto.Maintenance.Ongoing);
        Assert.Single(dto.Maintenance.Upcoming);
        Assert.Single(dto.Announcements);
    }
}
