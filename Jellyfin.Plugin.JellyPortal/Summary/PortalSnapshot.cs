namespace Jellyfin.Plugin.JellyPortal.Summary;

/// <summary>A service's status as the portal reports it. <see cref="Unknown"/> is any value this plugin does not know.</summary>
public enum ServiceStatus
{
    /// <summary>Unrecognised status: treated as healthy, shown neutral.</summary>
    Unknown,

    /// <summary>Healthy.</summary>
    Operational,

    /// <summary>Healthy but slower than its threshold: purely cosmetic.</summary>
    Slow,

    /// <summary>Degraded.</summary>
    Degraded,

    /// <summary>Under maintenance, by a window or by hand.</summary>
    Maintenance,

    /// <summary>Down.</summary>
    Down,
}

/// <summary>One service. The portal leaves out the services the admin excluded from the overall status.</summary>
/// <param name="Name">Full name, cut by the portal at 64 bytes.</param>
/// <param name="Status">Its status.</param>
public sealed record PortalService(string Name, ServiceStatus Status);

/// <summary>An open incident.</summary>
/// <param name="Title">Title.</param>
/// <param name="Status">investigating, identified or monitoring (passed through).</param>
/// <param name="Since">When it was opened.</param>
/// <param name="Services">Names of the services it covers; empty for an incident that covers none.</param>
public sealed record PortalIncident(string Title, string Status, DateTimeOffset? Since, IReadOnlyList<string> Services);

/// <summary>A maintenance window the portal still lists: in progress, or upcoming.</summary>
/// <param name="Title">Title.</param>
/// <param name="Description">Free text, one line.</param>
/// <param name="MarkedActive">The portal's own "in progress" flag. It only flips on its next check cycle, so it can lag.</param>
/// <param name="Starts">Start, UTC.</param>
/// <param name="Ends">End, UTC.</param>
/// <param name="Services">Names of the services it covers.</param>
public sealed record PortalMaintenance(
    string Title,
    string Description,
    bool MarkedActive,
    DateTimeOffset? Starts,
    DateTimeOffset? Ends,
    IReadOnlyList<string> Services);

/// <summary>An announcement showing right now.</summary>
/// <param name="Title">Title.</param>
/// <param name="Message">Plain one-line text.</param>
/// <param name="Type">info, warning, critical or success.</param>
/// <param name="Pinned">Pinned by the admin.</param>
public sealed record PortalAnnouncement(string Title, string Message, string Type, bool Pinned);

/// <summary>Everything one read of the portal's display-device API gave, in this plugin's terms.</summary>
/// <param name="PortalNow">The portal's clock when it answered. Maintenance times are in the portal's frame, so this is the reference.</param>
/// <param name="SiteName">The portal's site name.</param>
/// <param name="Services">The services listed, worst first.</param>
/// <param name="ServicesTotal">How many services the portal counts; larger than <paramref name="Services"/> when the list was cut.</param>
/// <param name="Incidents">Open incidents.</param>
/// <param name="Maintenance">Windows in progress and upcoming.</param>
/// <param name="Announcements">Announcements showing now.</param>
public sealed record PortalSnapshot(
    DateTimeOffset PortalNow,
    string SiteName,
    IReadOnlyList<PortalService> Services,
    int ServicesTotal,
    IReadOnlyList<PortalIncident> Incidents,
    IReadOnlyList<PortalMaintenance> Maintenance,
    IReadOnlyList<PortalAnnouncement> Announcements);
