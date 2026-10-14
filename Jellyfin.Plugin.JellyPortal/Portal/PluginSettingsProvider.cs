namespace Jellyfin.Plugin.JellyPortal.Portal;

/// <summary>Reads the settings from the plugin's stored configuration.</summary>
public sealed class PluginSettingsProvider : ISettingsProvider
{
    /// <inheritdoc />
    public JellyPortalSettings Current =>
        Plugin.Instance?.Configuration.ToSettings()
        ?? JellyPortalSettings.From(null, null, null, 0, 0, 0, null, false);
}
