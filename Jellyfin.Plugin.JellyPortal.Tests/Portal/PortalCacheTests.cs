using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;
using Jellyfin.Plugin.JellyPortal.Tests.Summary;
using Microsoft.Extensions.Logging.Abstractions;
using static Jellyfin.Plugin.JellyPortal.Tests.Portal.Settings;

namespace Jellyfin.Plugin.JellyPortal.Tests.Portal;

public class PortalCacheTests
{
    private readonly ManualTime _clock = new(Builders.Now);

    private PortalCache Cache(CountingClient client) => new(client, _clock, NullLogger<PortalCache>.Instance);

    private CountingClient Healthy() => new(_ => Task.FromResult(Reading(Builders.Snapshot(), _clock.GetUtcNow())));

    [Fact]
    public async Task ManyBrowsersInsideTheLifetimeCostOneRequest()
    {
        var client = Healthy();
        var cache = Cache(client);
        for (var i = 0; i < 50; i++)
        {
            await cache.GetAsync(Configured(), CancellationToken.None);
            _clock.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task AfterTheLifetimeThePortalIsAskedAgainOnce()
    {
        var client = Healthy();
        var cache = Cache(client);
        await cache.GetAsync(Configured(cacheSeconds: 30), CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(29));
        await cache.GetAsync(Configured(cacheSeconds: 30), CancellationToken.None);
        Assert.Equal(1, client.Calls);
        _clock.Advance(TimeSpan.FromSeconds(2));
        await cache.GetAsync(Configured(cacheSeconds: 30), CancellationToken.None);
        await cache.GetAsync(Configured(cacheSeconds: 30), CancellationToken.None);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task ConcurrentBrowsersShareTheRequestInFlight()
    {
        var release = new TaskCompletionSource();
        var client = new CountingClient(async _ =>
        {
            await release.Task;
            return Reading(Builders.Snapshot(), _clock.GetUtcNow());
        });
        var cache = Cache(client);
        var waiting = Enumerable.Range(0, 100).Select(_ => cache.GetAsync(Configured(), CancellationToken.None)).ToList();
        release.SetResult();
        var readings = await Task.WhenAll(waiting);
        Assert.Equal(1, client.Calls);
        Assert.All(readings, r => Assert.Same(readings[0], r));
    }

    [Fact]
    public async Task AFailureIsKeptForTheSameLifetimeSoADeadPortalCostsOneTimeoutNotOnePerBrowser()
    {
        var client = new CountingClient(_ => Task.FromResult(Failure(SummaryFailure.Timeout, _clock.GetUtcNow())));
        var cache = Cache(client);
        for (var i = 0; i < 20; i++)
        {
            var reading = await cache.GetAsync(Configured(), CancellationToken.None);
            Assert.Equal(SummaryFailure.Timeout, reading.Outcome.Failure);
        }

        Assert.Equal(1, client.Calls);
        _clock.Advance(TimeSpan.FromSeconds(31));
        await cache.GetAsync(Configured(), CancellationToken.None);
        Assert.Equal(2, client.Calls);
    }

    [Theory]
    [InlineData("http://other.example:5000", "k-secret")]
    [InlineData(Url, "another-key")]
    public async Task ChangingTheAddressOrTheKeyDropsTheKeptReadingAtOnce(string url, string key)
    {
        var client = Healthy();
        var cache = Cache(client);
        await cache.GetAsync(Configured(), CancellationToken.None);
        await cache.GetAsync(Configured(url: url, key: key), CancellationToken.None);
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task WithoutSettingsThePortalIsNeverAskedAndNothingIsKept()
    {
        var client = Healthy();
        var cache = Cache(client);
        var reading = await cache.GetAsync(Configured(key: ""), CancellationToken.None);
        Assert.Equal(SummaryFailure.NotConfigured, reading.Outcome.Failure);
        Assert.Equal(0, client.Calls);
        await cache.GetAsync(Configured(), CancellationToken.None);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task OneBrowserGivingUpDoesNotCancelTheRequestOthersWaitFor()
    {
        var release = new TaskCompletionSource();
        var client = new CountingClient(async _ =>
        {
            await release.Task;
            return Reading(Builders.Snapshot(), _clock.GetUtcNow());
        });
        var cache = Cache(client);
        using var gone = new CancellationTokenSource();
        var impatient = cache.GetAsync(Configured(), gone.Token);
        var patient = cache.GetAsync(Configured(), CancellationToken.None);
        gone.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => impatient);
        release.SetResult();
        Assert.NotNull((await patient).Outcome.Snapshot);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task AnUnexpectedErrorInTheClientBecomesABadResponseNotACrash()
    {
        var client = new CountingClient(_ => throw new InvalidOperationException("bug"));
        var reading = await Cache(client).GetAsync(Configured(), CancellationToken.None);
        Assert.Equal(SummaryFailure.BadResponse, reading.Outcome.Failure);
    }

    [Fact]
    public async Task ClearingForgetsTheKeptReading()
    {
        var client = Healthy();
        var cache = Cache(client);
        await cache.GetAsync(Configured(), CancellationToken.None);
        cache.Clear();
        await cache.GetAsync(Configured(), CancellationToken.None);
        Assert.Equal(2, client.Calls);
    }
}
