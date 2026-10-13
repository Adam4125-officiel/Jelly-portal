using System.Text.Json;

namespace Jellyfin.Plugin.JellyPortal.Summary;

/// <summary>The outcome of reading a portal answer: a snapshot, or the reason it is unusable.</summary>
/// <param name="Snapshot">The snapshot when the answer was usable.</param>
/// <param name="Failure">Why it was not.</param>
public sealed record ParseOutcome(PortalSnapshot? Snapshot, SummaryFailure? Failure)
{
    /// <summary>Builds a successful outcome.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The outcome.</returns>
    public static ParseOutcome Ok(PortalSnapshot snapshot) => new(snapshot, null);

    /// <summary>Builds a failed outcome.</summary>
    /// <param name="failure">The reason.</param>
    /// <returns>The outcome.</returns>
    public static ParseOutcome Fail(SummaryFailure failure) => new(null, failure);
}

/// <summary>
/// Reads the Status Portal's <c>GET /api/device/summary</c> answer to
/// <c>?sections=services,incidents,maintenance,announcements&amp;services=all&amp;detail=full</c>.
/// The contract lives in the portal repository (<c>device_api.py</c>); this reads exactly what it
/// documents and is tolerant of anything extra. It is deliberately forgiving about a single odd item
/// (skipped) and strict about the shape of the whole (a missing section is a bad answer).
/// </summary>
public static class DevicePayloadParser
{
    /// <summary>The schema version of the portal answer this reads.</summary>
    public const int SupportedVersion = 1;

    /// <summary>Parses a portal answer.</summary>
    /// <param name="json">The response body.</param>
    /// <returns>A snapshot, or the reason the body cannot be used.</returns>
    public static ParseOutcome Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return ParseOutcome.Fail(SummaryFailure.BadResponse);
        }
    }

    private static ParseOutcome Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || Int(root, "v") != SupportedVersion)
        {
            return ParseOutcome.Fail(SummaryFailure.BadResponse);
        }

        // A portal that predates detail=full ignores the parameter and answers in the screen-sized form,
        // whose names are cut at 24 bytes and whose service lists are one cut string: unusable here.
        if (Str(root, "detail") != "full")
        {
            return ParseOutcome.Fail(SummaryFailure.PortalOutdated);
        }

        var now = PortalTime.Parse(Str(root, "now"));
        if (now is null
            || !root.TryGetProperty("services", out var servicesSection) || servicesSection.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("incidents", out var incidentsSection) || incidentsSection.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("maintenance", out var maintenanceSection) || maintenanceSection.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("announcements", out var announcementsSection) || announcementsSection.ValueKind != JsonValueKind.Object)
        {
            return ParseOutcome.Fail(SummaryFailure.BadResponse);
        }

        var services = Items(servicesSection)
            .Select(item => new PortalService(Str(item, "name"), Status(Str(item, "status"))))
            .Where(service => service.Name.Length > 0)
            .ToList();

        var incidents = Items(incidentsSection)
            .Select(item => new PortalIncident(
                Str(item, "title"),
                Str(item, "status"),
                PortalTime.Parse(Str(item, "since")),
                Names(item, "service_names")))
            .ToList();

        var maintenance = Items(maintenanceSection)
            .Select(item => new PortalMaintenance(
                Str(item, "title"),
                Str(item, "description"),
                Str(item, "state") == "active",
                PortalTime.Parse(Str(item, "starts")),
                PortalTime.Parse(Str(item, "ends")),
                Names(item, "service_names")))
            .ToList();

        var announcements = Items(announcementsSection)
            .Select(item => new PortalAnnouncement(Str(item, "title"), Str(item, "text"), Str(item, "type"), Bool(item, "pinned")))
            .ToList();

        return ParseOutcome.Ok(new PortalSnapshot(
            now.Value,
            Str(root, "site"),
            services,
            Math.Max(Int(servicesSection, "total"), services.Count),
            incidents,
            maintenance,
            announcements));
    }

    private static IEnumerable<JsonElement> Items(JsonElement section) =>
        section.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static List<string> Names(JsonElement item, string name)
    {
        var result = new List<string>();
        if (item.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in array.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString()))
                {
                    result.Add(entry.GetString()!);
                }
            }
        }

        return result;
    }

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static ServiceStatus Status(string status) => status.ToLowerInvariant() switch
    {
        "operational" => ServiceStatus.Operational,
        "slow" => ServiceStatus.Slow,
        "degraded" => ServiceStatus.Degraded,
        "maintenance" => ServiceStatus.Maintenance,
        "down" => ServiceStatus.Down,
        _ => ServiceStatus.Unknown,
    };
}
