namespace Jellyfin.Plugin.JellyPortal.Summary;

/// <summary>
/// Turns one reading of the portal into the state of the icon and the content of the panel.
/// Pure: no clock, no network, no Jellyfin type. The portal's own <c>overall</c> is deliberately not
/// used, because its precedence (degraded above maintenance) is not this plugin's.
/// </summary>
public static class SummaryCalculator
{
    /// <summary>At most this many upcoming maintenances are listed.</summary>
    public const int MaxUpcoming = 10;

    /// <summary>Computes the summary.</summary>
    /// <param name="snapshot">What the portal said.</param>
    /// <param name="asOf">The portal's clock now (see <see cref="PortalTime.EstimatePortalNow"/>).</param>
    /// <param name="options">The settings.</param>
    /// <param name="publicUrl">The public address of the portal, for the link. Blank means no link.</param>
    /// <param name="fetchedAt">When the portal was read, on this machine's clock.</param>
    /// <param name="pollSeconds">The configured polling interval, passed on to the browser.</param>
    /// <returns>The summary.</returns>
    public static SummaryDto Calculate(
        PortalSnapshot snapshot,
        DateTimeOffset asOf,
        SummaryOptions options,
        string publicUrl,
        DateTimeOffset fetchedAt,
        int pollSeconds = 60)
    {
        var priority = new HashSet<string>(options.PriorityServices, StringComparer.OrdinalIgnoreCase);
        bool IsPriority(IEnumerable<string> names) => names.Any(priority.Contains);

        // The portal leaves out services the admin excluded from its overall status, and so must the icon.
        // Windows and incidents are not filtered by the portal, so one that names only services missing from
        // a complete service list is about an excluded service. When the list was cut (more services than the
        // portal sends) nothing can be inferred and everything counts.
        var listComplete = snapshot.ServicesTotal <= snapshot.Services.Count;
        var known = new HashSet<string>(snapshot.Services.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        bool Counts(IReadOnlyList<string> names) => names.Count == 0 || !listComplete || names.Any(known.Contains);

        var ongoing = new List<(MaintenanceItemDto Item, bool Counts)>();
        var upcoming = new List<(MaintenanceItemDto Item, bool Counts)>();
        foreach (var window in snapshot.Maintenance)
        {
            // "In progress" is the portal's flag OR the clock being inside the window: the flag only flips on
            // the portal's next check cycle, so right after a start it still says upcoming.
            var insideByClock = window.Starts is { } start && window.Ends is { } end && start <= asOf && asOf < end;
            var isOngoing = window.MarkedActive || insideByClock;
            if (!isOngoing && window.Starts is { } begins && begins <= asOf)
            {
                continue; // over, and the portal has not cleared it yet
            }

            var inWindow = !isOngoing && window.Starts is { } plannedStart && plannedStart - asOf <= options.SoonWindow;
            var item = new MaintenanceItemDto
            {
                Title = window.Title,
                Description = window.Description,
                StartsAt = window.Starts is { } s ? PortalTime.Format(s) : null,
                EndsAt = window.Ends is { } e ? PortalTime.Format(e) : null,
                Services = window.Services,
                Priority = IsPriority(window.Services),
                InWindow = inWindow,
            };
            (isOngoing ? ongoing : upcoming).Add((item, Counts(window.Services)));
        }

        var openIncidents = snapshot.Incidents
            .Select(i => (Dto: new IncidentDto
            {
                Title = i.Title,
                Status = i.Status,
                Since = i.Since is { } since ? PortalTime.Format(since) : null,
                Services = i.Services,
                Priority = IsPriority(i.Services),
            }, Counts: Counts(i.Services)))
            .ToList();

        var down = snapshot.Services.Any(s => s.Status == ServiceStatus.Down);
        var inMaintenance = ongoing.Any(m => m.Counts) || snapshot.Services.Any(s => s.Status == ServiceStatus.Maintenance);
        var degraded = snapshot.Services.Any(s => s.Status == ServiceStatus.Degraded) || openIncidents.Any(i => i.Counts);
        var soon = upcoming.Any(m => m.Counts && m.Item.InWindow);

        // Slow is cosmetic and an unrecognised status is not a problem: both fall through to Ok.
        var state = down ? SummaryState.Down
            : inMaintenance ? SummaryState.MaintenanceOngoing
            : degraded ? SummaryState.Degraded
            : soon ? SummaryState.MaintenanceSoon
            : SummaryState.Ok;

        return new SummaryDto
        {
            State = state.Name(),
            Now = PortalTime.Format(asOf),
            PollSeconds = pollSeconds,
            Portal = new PortalInfoDto { Name = snapshot.SiteName, Url = LinkOrNull(publicUrl), FetchedAt = PortalTime.Format(fetchedAt) },
            Services = snapshot.Services
                .Select(s => new ServiceDto { Name = s.Name, Status = StatusName(s.Status), Priority = priority.Contains(s.Name) })
                .ToList(),
            Incidents = openIncidents.Select(i => i.Dto).ToList(),
            Maintenance = new MaintenanceDto
            {
                Ongoing = ongoing.Select(m => m.Item)
                    .OrderByDescending(m => m.Priority)
                    .ThenBy(m => m.EndsAt ?? "9")
                    .ToList(),
                Upcoming = upcoming.Select(m => m.Item)
                    .OrderByDescending(m => m.Priority)
                    .ThenBy(m => m.StartsAt ?? "9")
                    .Take(MaxUpcoming)
                    .ToList(),
            },
            Announcements = snapshot.Announcements
                .Select(a => new AnnouncementDto { Title = a.Title, Message = a.Message, Type = a.Type, Pinned = a.Pinned })
                .ToList(),
        };
    }

    /// <summary>The summary when the portal could not be read: no data, a state of unknown and a code that says why.</summary>
    /// <param name="reason">Why.</param>
    /// <param name="publicUrl">The public address, if any, so the panel can still link to it.</param>
    /// <param name="utcNow">This machine's clock.</param>
    /// <param name="pollSeconds">The configured polling interval, passed on to the browser.</param>
    /// <returns>The summary.</returns>
    public static SummaryDto Unavailable(SummaryFailure reason, string publicUrl, DateTimeOffset utcNow, int pollSeconds = 60) => new()
    {
        State = SummaryState.Unknown.Name(),
        Reason = reason.Name(),
        Now = PortalTime.Format(utcNow),
        PollSeconds = pollSeconds,
        Portal = new PortalInfoDto { Url = LinkOrNull(publicUrl) },
    };

    private static string? LinkOrNull(string publicUrl) => string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.Trim();

    private static string StatusName(ServiceStatus status) => status switch
    {
        ServiceStatus.Operational => "operational",
        ServiceStatus.Slow => "slow",
        ServiceStatus.Degraded => "degraded",
        ServiceStatus.Maintenance => "maintenance",
        ServiceStatus.Down => "down",
        _ => "unknown",
    };
}
