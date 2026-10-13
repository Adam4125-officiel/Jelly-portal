using System.Globalization;

namespace Jellyfin.Plugin.JellyPortal.Summary;

/// <summary>Time helpers shared by the parser and the calculator.</summary>
public static class PortalTime
{
    /// <summary>
    /// Reads a timestamp from the portal. It sends <c>YYYY-MM-DDTHH:MM:SSZ</c>; a value without a zone
    /// is taken as UTC, which is what the portal's own maintenance form documents.
    /// </summary>
    /// <param name="value">The text, or null.</param>
    /// <returns>The instant, or null when blank or unreadable.</returns>
    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Formats an instant the way the JSON carries it: UTC, to the second, with a Z.</summary>
    /// <param name="value">The instant.</param>
    /// <returns>For example 2026-10-13T20:42:10Z.</returns>
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Where the portal's clock is now, judged from one reading of it. Maintenance times are in the portal's
    /// frame, so comparing them with this machine's clock would be wrong by however far the two clocks
    /// disagree. The portal said what time it was when it answered; the time since then is measured here,
    /// and only that elapsed time (not this machine's absolute clock) is trusted.
    /// </summary>
    /// <param name="portalNow">The portal's <c>now</c> in the reading.</param>
    /// <param name="readAt">This machine's clock when the reading arrived.</param>
    /// <param name="utcNow">This machine's clock now.</param>
    /// <returns>The portal's clock, estimated for this instant.</returns>
    public static DateTimeOffset EstimatePortalNow(DateTimeOffset portalNow, DateTimeOffset readAt, DateTimeOffset utcNow)
    {
        var elapsed = utcNow - readAt;
        return portalNow + (elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero);
    }
}
