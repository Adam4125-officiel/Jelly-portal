using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyPortal.Portal;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyPortal.Configuration;

/// <summary>
/// What the administrator sets in the dashboard. Stored by Jellyfin as XML in its plugin configuration
/// folder. Use <see cref="ToSettings"/> to read it: the raw values may be out of range.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the address of the Status Portal, for example http://192.168.1.10:5000.</summary>
    public string PortalUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the address people are sent to by the panel's "open the status portal" link, for example
    /// https://status.example.com. Optional, and not the address the plugin asks the portal with: that one is often
    /// a private address (a machine on the local network) that nobody else can reach and that is nobody's business.
    /// Blank means the panel has no link.
    /// </summary>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the portal's display-device API key. <see cref="JsonIgnoreAttribute"/> keeps it out of
    /// every JSON answer, including Jellyfin's own GET /Plugins/{id}/Configuration, while the XML store
    /// (which ignores that attribute) still keeps it. The settings page therefore never receives it, and
    /// reads and writes it through this plugin's own endpoints.
    /// </summary>
    [JsonIgnore]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets how often, in seconds, a browser asks for the summary.</summary>
    public int PollSeconds { get; set; } = JellyPortalSettings.PollRange.Default;

    /// <summary>Gets or sets how long, in seconds, a reading of the portal is reused for every browser.</summary>
    public int CacheSeconds { get; set; } = JellyPortalSettings.CacheRange.Default;

    /// <summary>Gets or sets how many hours ahead a planned maintenance makes the clock badge appear.</summary>
    public int SoonWindowHours { get; set; } = 24;

    /// <summary>Gets or sets the priority services, one name per line.</summary>
    public string PriorityServices { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether only administrators see the icon.</summary>
    public bool AdminsOnly { get; set; }

    /// <summary>Returns the validated, clamped settings.</summary>
    /// <returns>The settings.</returns>
    public JellyPortalSettings ToSettings() => JellyPortalSettings.From(
        PortalUrl, PublicUrl, ApiKey, PollSeconds, CacheSeconds, SoonWindowHours, PriorityServices, AdminsOnly);
}
