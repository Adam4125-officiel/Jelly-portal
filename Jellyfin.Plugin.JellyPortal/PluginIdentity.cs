namespace Jellyfin.Plugin.JellyPortal;

/// <summary>
/// Constants that identify the plugin to Jellyfin. Kept free of any Jellyfin type so the
/// test project can read them without loading the server assemblies.
/// </summary>
public static class PluginIdentity
{
    /// <summary>
    /// The plugin GUID. It must never change: Jellyfin would treat every version as a
    /// different plugin. It is mirrored in build.yaml and CLAUDE.md, and a test keeps all
    /// three in step.
    /// </summary>
    public const string Id = "a72a6bca-0bfe-480c-946f-e0a3fcb2c746";

    /// <summary>The display name shown in the Jellyfin dashboard.</summary>
    public const string Name = "Jelly Portal";
}
