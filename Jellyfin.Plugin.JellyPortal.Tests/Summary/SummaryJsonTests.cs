using System.Text.Json;
using Jellyfin.Plugin.JellyPortal.Summary;
using static Jellyfin.Plugin.JellyPortal.Tests.Summary.Builders;

namespace Jellyfin.Plugin.JellyPortal.Tests.Summary;

/// <summary>The browser script depends on these exact names. Changing one is a change to the client too.</summary>
public class SummaryJsonTests
{
    private static JsonElement Serialised(SummaryDto dto) => JsonDocument.Parse(SummaryJson.Serialize(dto)).RootElement.Clone();

    private static IEnumerable<string> Keys(JsonElement element) => element.EnumerateObject().Select(p => p.Name);

    [Fact]
    public void TheTopLevelKeysAreTheContract()
    {
        var root = Serialised(Calc(Snapshot()));
        Assert.Equal(["schema", "state", "reason", "now", "portal", "services", "incidents", "maintenance", "announcements"], Keys(root));
        Assert.Equal(["name", "url", "fetchedAt"], Keys(root.GetProperty("portal")));
        Assert.Equal(["ongoing", "upcoming"], Keys(root.GetProperty("maintenance")));
    }

    [Fact]
    public void EachItemHasTheDocumentedKeys()
    {
        var snapshot = Snapshot(
            incidents: [Incident("i", "Jellyfin")],
            maintenance: [Active(TimeSpan.FromHours(1), "Jellyfin"), Upcoming(TimeSpan.FromHours(2), "Jellyfin")],
            announcements: [new PortalAnnouncement("t", "m", "info", true)]);
        var root = Serialised(Calc(snapshot));
        Assert.Equal(["name", "status", "priority"], Keys(root.GetProperty("services")[0]));
        Assert.Equal(["title", "status", "since", "services", "priority"], Keys(root.GetProperty("incidents")[0]));
        string[] window = ["title", "description", "startsAt", "endsAt", "services", "priority", "inWindow"];
        Assert.Equal(window, Keys(root.GetProperty("maintenance").GetProperty("ongoing")[0]));
        Assert.Equal(window, Keys(root.GetProperty("maintenance").GetProperty("upcoming")[0]));
        Assert.Equal(["title", "message", "type", "pinned"], Keys(root.GetProperty("announcements")[0]));
    }

    [Fact]
    public void NullsAreWrittenAsNullAndNotOmitted()
    {
        var root = Serialised(Calc(Snapshot()));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("reason").ValueKind);
        var unavailable = Serialised(SummaryCalculator.Unavailable(SummaryFailure.Timeout, "", Now));
        Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("portal").GetProperty("fetchedAt").ValueKind);
        Assert.Equal("timeout", unavailable.GetProperty("reason").GetString());
    }

    [Fact]
    public void TextWithMarkupIsCarriedAsPlainTextAndNeverInterpreted()
    {
        var hostile = "<img src=x onerror=alert(1)> & \"quotes\"";
        var dto = Calc(Snapshot(announcements: [new PortalAnnouncement(hostile, hostile, "info", false)]));
        var back = Serialised(dto).GetProperty("announcements")[0];
        Assert.Equal(hostile, back.GetProperty("title").GetString());
        Assert.Equal(hostile, back.GetProperty("message").GetString());
    }

    [Fact]
    public void NonAsciiTextSurvivesTheRoundTrip()
    {
        var dto = Calc(Snapshot([Svc("Café serveur \U0001F4A5")]));
        Assert.Equal("Café serveur \U0001F4A5", Serialised(dto).GetProperty("services")[0].GetProperty("name").GetString());
    }
}
