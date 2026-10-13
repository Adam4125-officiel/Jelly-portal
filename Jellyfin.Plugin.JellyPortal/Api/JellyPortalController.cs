using System.Net.Mime;
using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyPortal.Api;

/// <summary>The summary the header icon is drawn from.</summary>
[ApiController]
[Route("JellyPortal")]
public class JellyPortalController : ControllerBase
{
    private readonly SummaryService _summary;

    /// <summary>Initializes a new instance of the <see cref="JellyPortalController"/> class.</summary>
    /// <param name="summary">Builds the summary.</param>
    public JellyPortalController(SummaryService summary)
    {
        _summary = summary;
    }

    /// <summary>Gets the state of the services and the content of the panel.</summary>
    /// <param name="cancellationToken">Cancelled when the browser goes away.</param>
    /// <response code="200">The summary. A portal that cannot be read is a 200 with state "unknown" and a reason.</response>
    /// <response code="403">The plugin is set to administrators only and this user is not one.</response>
    /// <returns>The summary as JSON.</returns>
    [HttpGet("Summary")]
    [Authorize]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        // Administrators carry the Administrator role claim (the same one Jellyfin's own RequiresElevation policy checks).
        if (_summary.Settings.AdminsOnly && !User.IsInRole("Administrator"))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var summary = await _summary.GetAsync(cancellationToken).ConfigureAwait(false);
        Response.Headers.CacheControl = "no-store";

        // Serialised here, not by the host: the host's JSON options are PascalCase and are not ours to depend on.
        return Content(SummaryJson.Serialize(summary), MediaTypeNames.Application.Json);
    }
}
