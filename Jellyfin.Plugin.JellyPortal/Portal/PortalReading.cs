using Jellyfin.Plugin.JellyPortal.Summary;

namespace Jellyfin.Plugin.JellyPortal.Portal;

/// <summary>One attempt to read the portal: what came back, and when.</summary>
/// <param name="Outcome">A snapshot, or the reason there is none.</param>
/// <param name="ReadAt">This machine's clock when the attempt finished. Only differences from it are ever used.</param>
public sealed record PortalReading(ParseOutcome Outcome, DateTimeOffset ReadAt);

/// <summary>Reads the portal.</summary>
public interface IPortalClient
{
    /// <summary>Asks the portal for its summary once.</summary>
    /// <param name="settings">The address and key.</param>
    /// <param name="cancellationToken">Cancelled when the caller gives up.</param>
    /// <returns>The reading. It never throws for a portal problem.</returns>
    Task<PortalReading> ReadAsync(JellyPortalSettings settings, CancellationToken cancellationToken);
}
