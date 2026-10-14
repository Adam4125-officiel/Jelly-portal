using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyPortal.Summary;

/// <summary>
/// What <c>GET /JellyPortal/Summary</c> returns. Every property is named explicitly: Jellyfin's own JSON
/// options are PascalCase and may change, and a browser script depends on these exact names. All times
/// are UTC, to the second, with a Z.
/// </summary>
public sealed class SummaryDto
{
    /// <summary>Gets the contract version; bump on a breaking change to this shape.</summary>
    [JsonPropertyName("schema")]
    public int Schema { get; init; } = 1;

    /// <summary>Gets the state the icon shows: down, maintenance_ongoing, degraded, maintenance_soon, ok or unknown.</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = "unknown";

    /// <summary>Gets why the state is unknown (not_configured, device_api_disabled, bad_api_key, unreachable, timeout, bad_response, portal_outdated), otherwise null.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>Gets the clock all other times are to be read against: the portal's, estimated for this instant.</summary>
    [JsonPropertyName("now")]
    public string Now { get; init; } = string.Empty;

    /// <summary>Gets how often, in seconds, the browser should ask again. A running page follows a changed setting at its next poll.</summary>
    [JsonPropertyName("pollSeconds")]
    public int PollSeconds { get; init; } = 60;

    /// <summary>Gets where this came from.</summary>
    [JsonPropertyName("portal")]
    public PortalInfoDto Portal { get; init; } = new();

    /// <summary>Gets the services the portal lists, worst first.</summary>
    [JsonPropertyName("services")]
    public IReadOnlyList<ServiceDto> Services { get; init; } = [];

    /// <summary>Gets the open incidents, newest first.</summary>
    [JsonPropertyName("incidents")]
    public IReadOnlyList<IncidentDto> Incidents { get; init; } = [];

    /// <summary>Gets the maintenance windows.</summary>
    [JsonPropertyName("maintenance")]
    public MaintenanceDto Maintenance { get; init; } = new();

    /// <summary>Gets the announcements showing now.</summary>
    [JsonPropertyName("announcements")]
    public IReadOnlyList<AnnouncementDto> Announcements { get; init; } = [];
}

/// <summary>The portal a summary was read from.</summary>
public sealed class PortalInfoDto
{
    /// <summary>Gets the portal's site name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the address to open from the panel: the administrator's public address, or null when there is none (no
    /// link). Never the address the portal is asked with, which can be a private one.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Gets when the portal was last read successfully, or null.</summary>
    [JsonPropertyName("fetchedAt")]
    public string? FetchedAt { get; init; }
}

/// <summary>A service row of the panel.</summary>
public sealed class ServiceDto
{
    /// <summary>Gets the name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets operational, slow, degraded, maintenance, down or unknown.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "unknown";

    /// <summary>Gets a value indicating whether the admin listed it as a priority service.</summary>
    [JsonPropertyName("priority")]
    public bool Priority { get; init; }
}

/// <summary>An open incident.</summary>
public sealed class IncidentDto
{
    /// <summary>Gets the title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets investigating, identified or monitoring.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets when it started, or null.</summary>
    [JsonPropertyName("since")]
    public string? Since { get; init; }

    /// <summary>Gets the services it covers.</summary>
    [JsonPropertyName("services")]
    public IReadOnlyList<string> Services { get; init; } = [];

    /// <summary>Gets a value indicating whether it touches a priority service.</summary>
    [JsonPropertyName("priority")]
    public bool Priority { get; init; }
}

/// <summary>The maintenance windows, split by what the panel needs.</summary>
public sealed class MaintenanceDto
{
    /// <summary>Gets the windows under way.</summary>
    [JsonPropertyName("ongoing")]
    public IReadOnlyList<MaintenanceItemDto> Ongoing { get; init; } = [];

    /// <summary>Gets the windows still to come, priority ones first and then the soonest.</summary>
    [JsonPropertyName("upcoming")]
    public IReadOnlyList<MaintenanceItemDto> Upcoming { get; init; } = [];
}

/// <summary>One maintenance window.</summary>
public sealed class MaintenanceItemDto
{
    /// <summary>Gets the title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the description, possibly empty.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the start, or null when the portal's value could not be read.</summary>
    [JsonPropertyName("startsAt")]
    public string? StartsAt { get; init; }

    /// <summary>Gets the end, or null.</summary>
    [JsonPropertyName("endsAt")]
    public string? EndsAt { get; init; }

    /// <summary>Gets the services it covers.</summary>
    [JsonPropertyName("services")]
    public IReadOnlyList<string> Services { get; init; } = [];

    /// <summary>Gets a value indicating whether it touches a priority service.</summary>
    [JsonPropertyName("priority")]
    public bool Priority { get; init; }

    /// <summary>Gets a value indicating whether an upcoming window starts inside the configured window (always false for an ongoing one).</summary>
    [JsonPropertyName("inWindow")]
    public bool InWindow { get; init; }
}

/// <summary>An announcement.</summary>
public sealed class AnnouncementDto
{
    /// <summary>Gets the title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the text: plain, never HTML.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>Gets info, warning, critical or success.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "info";

    /// <summary>Gets a value indicating whether the admin pinned it.</summary>
    [JsonPropertyName("pinned")]
    public bool Pinned { get; init; }
}

/// <summary>Serialisation of the summary, independent of the host's JSON settings.</summary>
public static class SummaryJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>Serialises a summary.</summary>
    /// <param name="summary">The summary.</param>
    /// <returns>Compact JSON.</returns>
    public static string Serialize(SummaryDto summary) => JsonSerializer.Serialize(summary, Options);
}
