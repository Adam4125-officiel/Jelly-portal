using System.Globalization;

namespace Jellyfin.Plugin.JellyPortal.Injection;

/// <summary>
/// Adds the plugin's stylesheet and script to Jellyfin's <c>index.html</c>. Pure string work, so it is tested
/// without a server. The addresses are relative to the page (<c>/web/</c>): that keeps working when Jellyfin
/// is served under a base URL such as <c>/jellyfin</c>, which an absolute <c>/JellyPortal/…</c> would break.
/// </summary>
public static class IndexHtmlInjector
{
    /// <summary>Marks a page that already carries the injection, so it is never added twice.</summary>
    public const string Marker = "data-jellyportal";

    /// <summary>Adds the stylesheet and the script.</summary>
    /// <param name="html">The page as Jellyfin serves it.</param>
    /// <param name="version">The plugin version, appended to the addresses so a browser never reuses a script from another version.</param>
    /// <returns>The page with the injection, or the page unchanged if it already has it or has no <c>&lt;/head&gt;</c> and <c>&lt;/body&gt;</c> to anchor on.</returns>
    public static string Inject(string html, string version)
    {
        if (string.IsNullOrEmpty(html) || html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        var head = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        var body = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (head < 0 || body < 0 || body < head)
        {
            return html;
        }

        var v = Uri.EscapeDataString(version);
        var link = string.Create(CultureInfo.InvariantCulture, $"<link rel=\"stylesheet\" href=\"../JellyPortal/client.css?v={v}\" {Marker}>");
        var script = string.Create(CultureInfo.InvariantCulture, $"<script defer src=\"../JellyPortal/client.js?v={v}\" {Marker}></script>");

        // The later position first, so the earlier index is still valid.
        return html.Insert(body, script).Insert(head, link);
    }
}
