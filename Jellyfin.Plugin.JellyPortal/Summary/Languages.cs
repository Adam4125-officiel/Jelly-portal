namespace Jellyfin.Plugin.JellyPortal.Summary;

/// <summary>
/// The languages the plugin's texts exist in, plus "auto", which leaves the choice to each viewer's own Jellyfin
/// language (the browser decides, because only it knows it). English is the default and the fallback.
/// </summary>
public static class Languages
{
    /// <summary>The language used when nothing, or something unknown, is configured.</summary>
    public const string Default = "en";

    /// <summary>Gets every accepted value of the language setting.</summary>
    public static IReadOnlyList<string> All { get; } = ["en", "fr", "auto"];

    /// <summary>Tells whether a value is one of <see cref="All"/>, ignoring case and surrounding spaces.</summary>
    /// <param name="value">The value.</param>
    /// <returns>True when it is accepted.</returns>
    public static bool IsValid(string? value) => value is not null && All.Contains(value.Trim().ToLowerInvariant());

    /// <summary>Turns a value into an accepted one: itself, lower-cased, or the default.</summary>
    /// <param name="value">The value.</param>
    /// <returns>One of <see cref="All"/>.</returns>
    public static string Normalize(string? value) => IsValid(value) ? value!.Trim().ToLowerInvariant() : Default;
}
