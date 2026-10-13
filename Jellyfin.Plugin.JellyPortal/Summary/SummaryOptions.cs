namespace Jellyfin.Plugin.JellyPortal.Summary;

/// <summary>The settings the state calculation depends on.</summary>
public sealed class SummaryOptions
{
    /// <summary>The longest allowed "maintenance coming up" window.</summary>
    public static readonly TimeSpan MaxSoonWindow = TimeSpan.FromDays(30);

    /// <summary>The default "maintenance coming up" window.</summary>
    public static readonly TimeSpan DefaultSoonWindow = TimeSpan.FromHours(24);

    /// <summary>Gets how far ahead a planned maintenance makes the clock badge appear, for every service.</summary>
    public TimeSpan SoonWindow { get; init; } = DefaultSoonWindow;

    /// <summary>Gets the names of the services whose maintenance and incidents are put forward in the panel.</summary>
    public IReadOnlyCollection<string> PriorityServices { get; init; } = [];

    /// <summary>Turns a hand-typed list (one name per line) into names: trimmed, no blanks, no duplicates ignoring case.</summary>
    /// <param name="text">The text from the settings page.</param>
    /// <returns>The names, in the order typed.</returns>
    public static IReadOnlyList<string> ParsePriorityNames(string? text)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var name = line.Trim();
            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>Clamps hours from the settings page into a usable window (at least one hour).</summary>
    /// <param name="hours">The configured number of hours.</param>
    /// <returns>A window between one hour and <see cref="MaxSoonWindow"/>.</returns>
    public static TimeSpan WindowFromHours(int hours) =>
        TimeSpan.FromHours(Math.Clamp(hours, 1, (int)MaxSoonWindow.TotalHours));
}
