namespace Jellyfin.Plugin.JellyPortal.Summary;

/// <summary>
/// The one state the header icon shows, listed by decreasing priority: the first that applies wins.
/// </summary>
public enum SummaryState
{
    /// <summary>At least one service is down.</summary>
    Down,

    /// <summary>A maintenance is under way.</summary>
    MaintenanceOngoing,

    /// <summary>A service is degraded, or an incident is open.</summary>
    Degraded,

    /// <summary>A maintenance is planned inside the configured window.</summary>
    MaintenanceSoon,

    /// <summary>All is well.</summary>
    Ok,

    /// <summary>The portal could not be read: grey, never red.</summary>
    Unknown,
}

/// <summary>
/// Why the state is <see cref="SummaryState.Unknown"/>. Only a short code ever leaves the server:
/// the exception text stays in the log, because it can name internal addresses.
/// </summary>
public enum SummaryFailure
{
    /// <summary>No portal address or no API key is configured.</summary>
    NotConfigured,

    /// <summary>The portal answered 404: its display-device API is switched off.</summary>
    DeviceApiDisabled,

    /// <summary>The portal answered 401: the key is wrong.</summary>
    BadApiKey,

    /// <summary>The portal could not be reached at all.</summary>
    Unreachable,

    /// <summary>The portal did not answer in time.</summary>
    Timeout,

    /// <summary>The portal answered something that is not the expected summary.</summary>
    BadResponse,

    /// <summary>The portal answered, but is too old to honour <c>detail=full</c>.</summary>
    PortalOutdated,
}

/// <summary>Wire names, as the browser receives them.</summary>
public static class SummaryWire
{
    /// <summary>The state's name in the JSON.</summary>
    /// <param name="state">The state.</param>
    /// <returns>A snake_case name.</returns>
    public static string Name(this SummaryState state) => state switch
    {
        SummaryState.Down => "down",
        SummaryState.MaintenanceOngoing => "maintenance_ongoing",
        SummaryState.Degraded => "degraded",
        SummaryState.MaintenanceSoon => "maintenance_soon",
        SummaryState.Ok => "ok",
        _ => "unknown",
    };

    /// <summary>The failure's code in the JSON.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns>A snake_case code.</returns>
    public static string Name(this SummaryFailure failure) => failure switch
    {
        SummaryFailure.NotConfigured => "not_configured",
        SummaryFailure.DeviceApiDisabled => "device_api_disabled",
        SummaryFailure.BadApiKey => "bad_api_key",
        SummaryFailure.Unreachable => "unreachable",
        SummaryFailure.Timeout => "timeout",
        SummaryFailure.PortalOutdated => "portal_outdated",
        _ => "bad_response",
    };
}
