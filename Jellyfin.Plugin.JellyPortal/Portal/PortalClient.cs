using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.JellyPortal.Summary;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyPortal.Portal;

/// <summary>
/// Reads the Status Portal's display-device API. The request is fixed on purpose: it asks for the four
/// sections this plugin shows, with the full detail, and for nothing else. Never add <c>resources</c>,
/// <c>vms</c> or <c>jellyfin=1</c>: that API is not subject to the portal's public-visibility switches, and
/// this plugin shows its answer to every Jellyfin user.
/// </summary>
public sealed class PortalClient : IPortalClient, IDisposable
{
    /// <summary>The path and query of the request, character for character.</summary>
    public const string PathAndQuery = "api/device/summary?sections=services,incidents,maintenance,announcements&services=all&detail=full";

    /// <summary>The largest answer read; anything bigger is not a summary.</summary>
    public const int MaxBodyBytes = 1 << 20;

    /// <summary>How long the portal has to answer.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly ILogger<PortalClient> _logger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _timeout;

    /// <summary>Initializes a new instance of the <see cref="PortalClient"/> class.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="time">The clock.</param>
    public PortalClient(ILogger<PortalClient> logger, TimeProvider time)
        : this(logger, time, CreateHandler(), DefaultTimeout)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PortalClient"/> class with a given transport, for tests.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="time">The clock.</param>
    /// <param name="handler">The transport.</param>
    /// <param name="timeout">How long the portal has to answer.</param>
    public PortalClient(ILogger<PortalClient> logger, TimeProvider time, HttpMessageHandler handler, TimeSpan timeout)
    {
        _logger = logger;
        _time = time;
        _timeout = timeout;
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// Builds the transport. Redirects are not followed: a header such as the API key is forwarded across a
    /// redirect, and a portal address that bounces to another host must not receive the key.
    /// </summary>
    /// <returns>The handler.</returns>
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(4),
    };

    /// <summary>Builds the address to ask, keeping any path prefix the portal is served under.</summary>
    /// <param name="portalUrl">The configured address.</param>
    /// <param name="uri">The full request address.</param>
    /// <returns>False when the address cannot be used: not http(s), not absolute, or carrying credentials.</returns>
    public static bool TryBuildUri(string portalUrl, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(portalUrl.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || baseUri.UserInfo.Length > 0
            || baseUri.Query.Length > 0
            || baseUri.Fragment.Length > 0)
        {
            return false;
        }

        var withSlash = new UriBuilder(baseUri) { Path = baseUri.AbsolutePath.TrimEnd('/') + "/" }.Uri;
        uri = new Uri(withSlash, PathAndQuery);
        return true;
    }

    /// <inheritdoc />
    public async Task<PortalReading> ReadAsync(JellyPortalSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured || !TryBuildUri(settings.PortalUrl, out var uri))
        {
            return Done(SummaryFailure.NotConfigured);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.UserAgent.ParseAdd("JellyPortal");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("[JellyPortal] The portal answered 404: its display-device API is switched off, or this is not the portal's address.");
                return Done(SummaryFailure.DeviceApiDisabled);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("[JellyPortal] The portal refused the API key (401).");
                return Done(SummaryFailure.BadApiKey);
            }

            if (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            {
                _logger.LogWarning("[JellyPortal] The portal is unavailable (HTTP {Status}).", status);
                return Done(SummaryFailure.Unreachable);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[JellyPortal] The portal answered HTTP {Status}{Redirect}.",
                    status,
                    status is >= 300 and < 400 ? " (a redirect; redirects are not followed, set the address the portal really answers on)" : string.Empty);
                return Done(SummaryFailure.BadResponse);
            }

            var body = await ReadBodyAsync(response, timeout.Token).ConfigureAwait(false);
            if (body is null)
            {
                _logger.LogWarning("[JellyPortal] The portal's answer is larger than {Limit} bytes; ignored.", MaxBodyBytes);
                return Done(SummaryFailure.BadResponse);
            }

            var outcome = DevicePayloadParser.Parse(body);
            if (outcome.Failure is { } failure)
            {
                _logger.LogWarning("[JellyPortal] The portal's answer cannot be used: {Reason}.", failure.Name());
            }

            return new PortalReading(outcome, _time.GetUtcNow());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[JellyPortal] The portal did not answer within {Seconds} s.", _timeout.TotalSeconds);
            return Done(SummaryFailure.Timeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("[JellyPortal] The portal cannot be reached: {Message}", ex.InnerException?.Message ?? ex.Message);
            return Done(SummaryFailure.Unreachable);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private PortalReading Done(SummaryFailure failure) => new(ParseOutcome.Fail(failure), _time.GetUtcNow());
}
