using System.Net;
using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.JellyPortal.Tests.Portal;

/// <summary>A clock a test moves by hand.</summary>
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTime(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>A transport that answers with whatever the test says and remembers what it was asked.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

    public StubHandler(HttpStatusCode status, string body = "")
        : this((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }))
    {
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return _respond(request, cancellationToken);
    }
}

/// <summary>An <see cref="IPortalClient"/> that counts how often it was asked and returns a prepared reading.</summary>
internal sealed class CountingClient : IPortalClient
{
    private readonly Func<JellyPortalSettings, Task<PortalReading>> _read;

    public CountingClient(Func<JellyPortalSettings, Task<PortalReading>> read) => _read = read;

    public int Calls;

    public Task<PortalReading> ReadAsync(JellyPortalSettings settings, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return _read(settings);
    }
}

internal static class Settings
{
    public const string Url = "http://portal.example:5000";

    /// <summary>The address people are sent to: deliberately not the one the portal is asked with.</summary>
    public const string PublicUrl = "https://status.example.com";

    public static JellyPortalSettings Configured(
        int cacheSeconds = 30,
        string url = Url,
        string publicUrl = PublicUrl,
        string key = "k-secret",
        int soonHours = 24,
        string priority = "",
        bool adminsOnly = false) =>
        JellyPortalSettings.From(url, publicUrl, key, 60, cacheSeconds, soonHours, priority, adminsOnly);

    public static PortalReading Reading(PortalSnapshot snapshot, DateTimeOffset at) => new(ParseOutcome.Ok(snapshot), at);

    public static PortalReading Failure(SummaryFailure failure, DateTimeOffset at) => new(ParseOutcome.Fail(failure), at);

    public static PortalClient Client(StubHandler handler, TimeProvider time, TimeSpan? timeout = null) =>
        new(NullLogger<PortalClient>.Instance, time, handler, timeout ?? TimeSpan.FromSeconds(5));
}
