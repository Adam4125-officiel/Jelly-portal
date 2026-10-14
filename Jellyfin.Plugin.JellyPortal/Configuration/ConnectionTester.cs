using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;

namespace Jellyfin.Plugin.JellyPortal.Configuration;

/// <summary>The outcome of "test the connection": data only, the settings page words it in its own language.</summary>
/// <param name="Ok">Whether the portal answered a usable summary.</param>
/// <param name="Reason">The failure code when it did not (a summary failure, or a refused setting such as invalid_portal_url).</param>
/// <param name="SiteName">The portal's site name when it answered, otherwise null.</param>
/// <param name="Services">How many services the portal reported.</param>
public sealed record ConnectionResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("siteName")] string? SiteName,
    [property: JsonPropertyName("services")] int Services);

/// <summary>Asks the portal once, now, with the settings as typed, and reports what came back.</summary>
public static class ConnectionTester
{
    /// <summary>Tests a connection. An explicit action of the administrator, so it may wait on the network.</summary>
    /// <param name="client">Reads the portal.</param>
    /// <param name="settings">The address and key to try.</param>
    /// <param name="cancellationToken">Cancelled when the administrator goes away.</param>
    /// <returns>The outcome.</returns>
    public static async Task<ConnectionResult> TestAsync(IPortalClient client, JellyPortalSettings settings, CancellationToken cancellationToken)
    {
        var reading = await client.ReadAsync(settings, cancellationToken).ConfigureAwait(false);
        if (reading.Outcome.Snapshot is { } snapshot)
        {
            return new ConnectionResult(true, null, snapshot.SiteName, snapshot.Services.Count);
        }

        var failure = reading.Outcome.Failure ?? SummaryFailure.BadResponse;
        return new ConnectionResult(false, failure.Name(), null, 0);
    }
}
