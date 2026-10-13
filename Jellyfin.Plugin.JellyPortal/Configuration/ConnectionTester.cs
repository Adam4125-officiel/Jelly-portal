using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;

namespace Jellyfin.Plugin.JellyPortal.Configuration;

/// <summary>The outcome of "test the connection".</summary>
/// <param name="Ok">Whether the portal answered a usable summary.</param>
/// <param name="Reason">The failure code when it did not.</param>
/// <param name="Message">A sentence for the administrator, in French, saying what to do.</param>
/// <param name="Services">How many services the portal reported.</param>
public sealed record ConnectionResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("services")] int Services);

/// <summary>Asks the portal once, now, with the settings as typed, and says in words what is wrong.</summary>
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
            var count = snapshot.Services.Count;
            return new ConnectionResult(true, null, $"Connexion réussie : le portal « {snapshot.SiteName} » suit {count} service{(count > 1 ? "s" : string.Empty)}.", count);
        }

        var failure = reading.Outcome.Failure ?? SummaryFailure.BadResponse;
        return new ConnectionResult(false, failure.Name(), Explain(failure), 0);
    }

    /// <summary>Says what a failure means and what to do about it.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns>A sentence in French.</returns>
    public static string Explain(SummaryFailure failure) => failure switch
    {
        SummaryFailure.NotConfigured => "Renseignez l'adresse du portal et la clé API.",
        SummaryFailure.DeviceApiDisabled => "Le portal répond 404 : son API « Display device » est désactivée (dans l'administration du portal, page Display device), ou cette adresse n'est pas celle du portal.",
        SummaryFailure.BadApiKey => "Le portal refuse la clé API (401). Copiez la clé affichée dans l'administration du portal, page Display device.",
        SummaryFailure.Unreachable => "Impossible de joindre le portal. Vérifiez l'adresse et le port, et que le serveur Jellyfin peut atteindre cette machine.",
        SummaryFailure.Timeout => "Le portal ne répond pas dans les 5 secondes.",
        SummaryFailure.PortalOutdated => "Ce portal est trop ancien : il faut la version 1.12.0 ou plus récente (paramètre detail=full).",
        _ => "Le portal a répondu quelque chose d'inattendu. Vérifiez que l'adresse est bien celle du status portal (et pas celle d'un proxy ou d'une autre application).",
    };
}
