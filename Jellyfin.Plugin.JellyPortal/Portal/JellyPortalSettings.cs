using Jellyfin.Plugin.JellyPortal.Summary;

namespace Jellyfin.Plugin.JellyPortal.Portal;

/// <summary>
/// The plugin's settings, already validated and clamped, as the rest of the code sees them.
/// Immutable on purpose: a reading is cached under the settings it was made with, so a changed
/// address or key is recognised by comparing two of these.
/// </summary>
/// <param name="PortalUrl">The address the plugin asks the portal with, trimmed. May be empty. Never leaves the server.</param>
/// <param name="PublicUrl">The address of the panel's link, trimmed. May be empty (no link). The only portal address a browser ever receives.</param>
/// <param name="ApiKey">The portal's display-device API key. Never leaves the server.</param>
/// <param name="PollSeconds">How often a browser asks for the summary.</param>
/// <param name="CacheSeconds">How long a reading of the portal is reused.</param>
/// <param name="SoonWindowHours">How far ahead a planned maintenance makes the clock badge appear.</param>
/// <param name="PriorityServices">Names of the priority services.</param>
/// <param name="AdminsOnly">Whether only administrators see the icon.</param>
public sealed record JellyPortalSettings(
    string PortalUrl,
    string PublicUrl,
    string ApiKey,
    int PollSeconds,
    int CacheSeconds,
    int SoonWindowHours,
    IReadOnlyList<string> PriorityServices,
    bool AdminsOnly)
{
    /// <summary>Gets the default and the allowed range of the browser polling interval.</summary>
    public static (int Default, int Min, int Max) PollRange => (60, 10, 3600);

    /// <summary>Gets the default and the allowed range of the server cache lifetime. Never zero: a cache of zero would let N browsers become N requests to the portal.</summary>
    public static (int Default, int Min, int Max) CacheRange => (30, 5, 600);

    /// <summary>Gets a value indicating whether there is an address and a key to ask the portal with.</summary>
    public bool IsConfigured => PortalUrl.Length > 0 && ApiKey.Length > 0;

    /// <summary>Gets the lifetime of a reading.</summary>
    public TimeSpan CacheTtl => TimeSpan.FromSeconds(CacheSeconds);

    /// <summary>Gets the options the state calculation needs.</summary>
    public SummaryOptions ToOptions() => new()
    {
        SoonWindow = SummaryOptions.WindowFromHours(SoonWindowHours),
        PriorityServices = PriorityServices,
    };

    /// <summary>Builds clamped settings from what the settings page stored.</summary>
    /// <param name="portalUrl">The address the portal is asked with, as typed.</param>
    /// <param name="publicUrl">The address of the panel's link, as typed.</param>
    /// <param name="apiKey">The key as typed.</param>
    /// <param name="pollSeconds">Polling interval.</param>
    /// <param name="cacheSeconds">Cache lifetime.</param>
    /// <param name="soonWindowHours">Window of the clock badge.</param>
    /// <param name="priorityServices">One name per line.</param>
    /// <param name="adminsOnly">Visibility.</param>
    /// <returns>Settings with every number inside its range.</returns>
    public static JellyPortalSettings From(
        string? portalUrl,
        string? publicUrl,
        string? apiKey,
        int pollSeconds,
        int cacheSeconds,
        int soonWindowHours,
        string? priorityServices,
        bool adminsOnly) => new(
            (portalUrl ?? string.Empty).Trim(),
            (publicUrl ?? string.Empty).Trim(),
            (apiKey ?? string.Empty).Trim(),
            Math.Clamp(pollSeconds, PollRange.Min, PollRange.Max),
            Math.Clamp(cacheSeconds, CacheRange.Min, CacheRange.Max),
            (int)SummaryOptions.WindowFromHours(soonWindowHours).TotalHours,
            SummaryOptions.ParsePriorityNames(priorityServices),
            adminsOnly);
}
