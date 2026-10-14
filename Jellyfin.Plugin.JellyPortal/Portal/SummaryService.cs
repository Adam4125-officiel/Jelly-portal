using Jellyfin.Plugin.JellyPortal.Summary;

namespace Jellyfin.Plugin.JellyPortal.Portal;

/// <summary>Where the current settings come from.</summary>
public interface ISettingsProvider
{
    /// <summary>Gets the validated settings as they are right now.</summary>
    JellyPortalSettings Current { get; }
}

/// <summary>Builds the summary a browser receives: the cached reading, with the state computed against the portal's clock now.</summary>
public sealed class SummaryService
{
    private readonly ISettingsProvider _settings;
    private readonly PortalCache _cache;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="SummaryService"/> class.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="cache">The cache in front of the portal.</param>
    /// <param name="time">The clock.</param>
    public SummaryService(ISettingsProvider settings, PortalCache cache, TimeProvider time)
    {
        _settings = settings;
        _cache = cache;
        _time = time;
    }

    /// <summary>Gets the current settings.</summary>
    public JellyPortalSettings Settings => _settings.Current;

    /// <summary>Builds the summary.</summary>
    /// <param name="cancellationToken">Cancelled when the caller gives up.</param>
    /// <returns>The summary. Never throws for a portal problem.</returns>
    public async Task<SummaryDto> GetAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var reading = await _cache.GetAsync(settings, cancellationToken).ConfigureAwait(false);
        var utcNow = _time.GetUtcNow();
        if (reading.Outcome.Snapshot is not { } snapshot)
        {
            return SummaryCalculator.Unavailable(reading.Outcome.Failure ?? SummaryFailure.BadResponse, settings.PublicUrl, utcNow, settings.PollSeconds);
        }

        // Recomputed on every request: a window that starts between two readings must turn
        // "coming up" into "in progress" on time, whatever the cache lifetime.
        var asOf = PortalTime.EstimatePortalNow(snapshot.PortalNow, reading.ReadAt, utcNow);
        return SummaryCalculator.Calculate(snapshot, asOf, settings.ToOptions(), settings.PublicUrl, reading.ReadAt, settings.PollSeconds);
    }
}
