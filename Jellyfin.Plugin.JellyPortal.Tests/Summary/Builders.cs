using Jellyfin.Plugin.JellyPortal.Summary;

namespace Jellyfin.Plugin.JellyPortal.Tests.Summary;

/// <summary>Short ways to write the portal data a test needs.</summary>
internal static class Builders
{
    /// <summary>The instant every test treats as "now" on the portal's clock.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 13, 20, 0, 0, TimeSpan.Zero);

    public static PortalService Svc(string name, ServiceStatus status = ServiceStatus.Operational) => new(name, status);

    public static PortalIncident Incident(string title = "Something is wrong", params string[] services) =>
        new(title, "investigating", Now.AddMinutes(-20), services);

    /// <summary>A window not yet started that the portal also lists as upcoming.</summary>
    public static PortalMaintenance Upcoming(TimeSpan startsIn, params string[] services) =>
        new("Planned work", "details", false, Now + startsIn, Now + startsIn + TimeSpan.FromHours(1), services);

    /// <summary>A window the portal itself marks as in progress.</summary>
    public static PortalMaintenance Active(TimeSpan endsIn, params string[] services) =>
        new("Work under way", "details", true, Now - TimeSpan.FromMinutes(30), Now + endsIn, services);

    public static PortalSnapshot Snapshot(
        IEnumerable<PortalService>? services = null,
        IEnumerable<PortalIncident>? incidents = null,
        IEnumerable<PortalMaintenance>? maintenance = null,
        IEnumerable<PortalAnnouncement>? announcements = null,
        int? total = null)
    {
        var list = (services ?? [Svc("Jellyfin")]).ToList();
        return new PortalSnapshot(
            Now,
            "Home",
            list,
            total ?? list.Count,
            (incidents ?? []).ToList(),
            (maintenance ?? []).ToList(),
            (announcements ?? []).ToList());
    }

    public static SummaryDto Calc(PortalSnapshot snapshot, SummaryOptions? options = null, DateTimeOffset? asOf = null) =>
        SummaryCalculator.Calculate(snapshot, asOf ?? Now, options ?? new SummaryOptions(), "https://portal.example", Now);
}
