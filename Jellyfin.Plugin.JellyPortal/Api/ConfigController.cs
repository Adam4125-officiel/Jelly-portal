using System.Net.Mime;
using Jellyfin.Plugin.JellyPortal.Configuration;
using Jellyfin.Plugin.JellyPortal.Injection;
using Jellyfin.Plugin.JellyPortal.Portal;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyPortal.Api;

/// <summary>
/// The settings page's own endpoints. Jellyfin's generic plugin-configuration API is not used because it would hand
/// the portal API key to the browser (and a page that reads and writes the whole configuration would wipe it).
/// </summary>
[ApiController]
[Route("JellyPortal/Config")]
[Authorize(Policy = Policies.RequiresElevation)]
public class ConfigController : ControllerBase
{
    private readonly PortalCache _cache;
    private readonly IPortalClient _client;
    private readonly InjectionStatus _injection;

    /// <summary>Initializes a new instance of the <see cref="ConfigController"/> class.</summary>
    /// <param name="cache">Forgotten when the settings change.</param>
    /// <param name="client">Used by "test the connection".</param>
    /// <param name="injection">How registering with File Transformation went.</param>
    public ConfigController(PortalCache cache, IPortalClient client, InjectionStatus injection)
    {
        _cache = cache;
        _client = client;
        _injection = injection;
    }

    /// <summary>Gets the settings, without the API key.</summary>
    /// <response code="200">The settings.</response>
    /// <returns>The settings as JSON.</returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Json(ConfigService.View(Current(), _injection.Result));

    /// <summary>Saves the settings.</summary>
    /// <param name="update">What the page submitted.</param>
    /// <response code="200">Saved; the answer is the settings as they are now (numbers may have been clamped).</response>
    /// <response code="400">Refused, with an error code the page words (invalid_portal_url, invalid_public_url, invalid_language).</response>
    /// <returns>The settings as JSON.</returns>
    [HttpPost]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Save([FromBody] ConfigUpdate update)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var result = ConfigService.Apply(plugin.Configuration, update);
        if (result.Configuration is null)
        {
            return BadRequest(new { error = result.Error });
        }

        plugin.UpdateConfiguration(result.Configuration);
        _cache.Clear();
        return Json(ConfigService.View(result.Configuration, _injection.Result));
    }

    /// <summary>Tries the portal now with the address and key as typed, saved or not.</summary>
    /// <param name="update">The address and key typed on the page; a blank key means the saved one.</param>
    /// <param name="cancellationToken">Cancelled when the page goes away.</param>
    /// <response code="200">The outcome, which is never an error status: a refusal by the portal is an answer.</response>
    /// <returns>The outcome as JSON.</returns>
    [HttpPost("Test")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Test([FromBody] ConfigUpdate update, CancellationToken cancellationToken)
    {
        var applied = ConfigService.Apply(Current(), update);
        if (applied.Configuration is null)
        {
            return Json(new ConnectionResult(false, applied.Error ?? "bad_response", null, 0));
        }

        var result = await ConnectionTester.TestAsync(_client, applied.Configuration.ToSettings(), cancellationToken).ConfigureAwait(false);
        return Json(result);
    }

    private static PluginConfiguration Current() => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    private ContentResult Json(object value) => Content(System.Text.Json.JsonSerializer.Serialize(value), MediaTypeNames.Application.Json);
}
