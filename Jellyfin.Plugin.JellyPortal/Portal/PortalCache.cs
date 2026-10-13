using Jellyfin.Plugin.JellyPortal.Summary;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyPortal.Portal;

/// <summary>
/// Keeps the last reading of the portal so that N browsers cost one request to it per lifetime.
/// A reading that failed is kept for the same time: a dead portal then costs one timeout per lifetime
/// instead of one per browser. Concurrent callers share the one request in flight. A changed address or
/// key makes the kept reading unusable at once.
/// </summary>
public sealed class PortalCache
{
    private readonly IPortalClient _client;
    private readonly TimeProvider _time;
    private readonly ILogger<PortalCache> _logger;
    private readonly object _gate = new();
    private (string Address, string Key, DateTimeOffset At, Task<PortalReading> Task)? _entry;

    /// <summary>Initializes a new instance of the <see cref="PortalCache"/> class.</summary>
    /// <param name="client">Reads the portal.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public PortalCache(IPortalClient client, TimeProvider time, ILogger<PortalCache> logger)
    {
        _client = client;
        _time = time;
        _logger = logger;
    }

    /// <summary>Gets the current reading, asking the portal only if the kept one is too old or for other settings.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">Cancels this caller's wait, never the shared request.</param>
    /// <returns>The reading.</returns>
    public async Task<PortalReading> GetAsync(JellyPortalSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            return new PortalReading(ParseOutcome.Fail(SummaryFailure.NotConfigured), _time.GetUtcNow());
        }

        Task<PortalReading> task;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (_entry is { } entry
                && entry.Address == settings.PortalUrl
                && entry.Key == settings.ApiKey
                && (!entry.Task.IsCompleted || now - entry.At < settings.CacheTtl))
            {
                task = entry.Task;
            }
            else
            {
                // The shared request must not be cancelled because one caller gave up.
                task = ReadSafelyAsync(settings);
                _entry = (settings.PortalUrl, settings.ApiKey, now, task);
            }
        }

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forgets the kept reading, for example after the settings were saved.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entry = null;
        }
    }

    private async Task<PortalReading> ReadSafelyAsync(JellyPortalSettings settings)
    {
        try
        {
            return await _client.ReadAsync(settings, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A reading must never throw into a request: whatever went wrong is a bad response.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "[JellyPortal] Unexpected error while reading the portal.");
            return new PortalReading(ParseOutcome.Fail(SummaryFailure.BadResponse), _time.GetUtcNow());
        }
    }
}
