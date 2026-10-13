using System.Reflection;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.JellyPortal.Api;

/// <summary>One file served to the browser.</summary>
/// <param name="Bytes">The content.</param>
/// <param name="ContentType">The media type.</param>
/// <param name="ETag">A quoted validator, so a browser revalidates instead of downloading again.</param>
public sealed record ClientAsset(byte[] Bytes, string ContentType, string ETag);

/// <summary>The script and stylesheet embedded in the plugin.</summary>
public static class ClientAssets
{
    private static readonly Lazy<Dictionary<string, ClientAsset>> Assets = new(Load);

    /// <summary>Finds an asset by its file name.</summary>
    /// <param name="name">client.js or client.css.</param>
    /// <returns>The asset, or null for any other name. Names are looked up, never joined to a path.</returns>
    public static ClientAsset? Find(string name) => Assets.Value.GetValueOrDefault(name);

    private static Dictionary<string, ClientAsset> Load() => new(StringComparer.Ordinal)
    {
        ["client.js"] = Read("Web.jellyportal.js", "text/javascript; charset=utf-8"),
        ["client.css"] = Read("Web.jellyportal.css", "text/css; charset=utf-8"),
    };

    private static ClientAsset Read(string resource, string contentType)
    {
        var assembly = typeof(ClientAssets).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{typeof(Plugin).Namespace}.{resource}")
            ?? throw new InvalidOperationException($"Embedded resource {resource} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant() + "\"";
        return new ClientAsset(bytes, contentType, etag);
    }
}
