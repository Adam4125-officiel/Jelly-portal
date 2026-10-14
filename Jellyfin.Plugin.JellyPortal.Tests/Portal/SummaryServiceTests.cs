using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;
using Jellyfin.Plugin.JellyPortal.Tests.Summary;
using Microsoft.Extensions.Logging.Abstractions;
using static Jellyfin.Plugin.JellyPortal.Tests.Portal.Settings;

namespace Jellyfin.Plugin.JellyPortal.Tests.Portal;

public class SummaryServiceTests
{
    private sealed class FixedSettings : ISettingsProvider
    {
        public JellyPortalSettings Current { get; set; } = Configured();
    }

    private readonly ManualTime _clock = new(Builders.Now);
    private readonly FixedSettings _settings = new();

    private SummaryService Service(CountingClient client) =>
        new(_settings, new PortalCache(client, _clock, NullLogger<PortalCache>.Instance), _clock);

    private CountingClient Returning(PortalSnapshot snapshot) =>
        new(_ => Task.FromResult(Reading(snapshot, _clock.GetUtcNow())));

    [Fact]
    public async Task WithoutSettingsTheStateIsUnknownAndSaysWhy()
    {
        _settings.Current = Configured(key: "");
        var dto = await Service(Returning(Builders.Snapshot())).GetAsync(CancellationToken.None);
        Assert.Equal("unknown", dto.State);
        Assert.Equal("not_configured", dto.Reason);
    }

    [Fact]
    public async Task AFailedReadingIsUnknownWithItsReasonAndThePublicAddressForTheLink()
    {
        var client = new CountingClient(_ => Task.FromResult(Failure(SummaryFailure.BadApiKey, _clock.GetUtcNow())));
        var dto = await Service(client).GetAsync(CancellationToken.None);
        Assert.Equal("unknown", dto.State);
        Assert.Equal("bad_api_key", dto.Reason);
        Assert.Equal(PublicUrl, dto.Portal.Url);
    }

    [Fact]
    public async Task AGoodReadingGivesTheComputedSummary()
    {
        var snapshot = Builders.Snapshot([Builders.Svc("Jellyfin", ServiceStatus.Degraded)]);
        var dto = await Service(Returning(snapshot)).GetAsync(CancellationToken.None);
        Assert.Equal("degraded", dto.State);
        Assert.Null(dto.Reason);
        Assert.Equal(PublicUrl, dto.Portal.Url);
    }

    [Fact]
    public async Task TheStateIsRecomputedAtEveryRequestSoAWindowStartsOnTimeWhateverTheCacheLifetime()
    {
        var snapshot = Builders.Snapshot(maintenance: [Builders.Upcoming(TimeSpan.FromMinutes(5), "Jellyfin")]);
        _settings.Current = Configured(cacheSeconds: 600);
        var client = Returning(snapshot);
        var service = Service(client);

        Assert.Equal("maintenance_soon", (await service.GetAsync(CancellationToken.None)).State);
        _clock.Advance(TimeSpan.FromMinutes(6));
        var later = await service.GetAsync(CancellationToken.None);

        Assert.Equal(1, client.Calls);                       // still the same cached reading...
        Assert.Equal("maintenance_ongoing", later.State);    // ...and the state has moved on
        Assert.Single(later.Maintenance.Ongoing);
    }

    [Fact]
    public async Task OnlyElapsedTimeIsTrustedNotThisMachinesAbsoluteClock()
    {
        // This machine thinks it is 2029; the portal says 2026-10-13 20:00 and a window starts at 20:05.
        _clock.Advance(TimeSpan.FromDays(365 * 3));
        _settings.Current = Configured(cacheSeconds: 600);
        var snapshot = Builders.Snapshot(maintenance: [Builders.Upcoming(TimeSpan.FromMinutes(5), "Jellyfin")]);
        var client = new CountingClient(_ => Task.FromResult(Reading(snapshot, _clock.GetUtcNow())));
        var service = Service(client);

        var first = await service.GetAsync(CancellationToken.None);
        Assert.Equal("maintenance_soon", first.State);
        Assert.Equal("2026-10-13T20:00:00Z", first.Now);

        _clock.Advance(TimeSpan.FromMinutes(6));
        var later = await service.GetAsync(CancellationToken.None);
        Assert.Equal(1, client.Calls);                     // the same reading, six minutes older
        Assert.Equal("maintenance_ongoing", later.State);
        Assert.Equal("2026-10-13T20:06:00Z", later.Now);   // the portal's clock moved on by the elapsed time only
    }

    [Fact]
    public async Task PriorityServicesAndTheWindowComeFromTheSettings()
    {
        _settings.Current = Configured(soonHours: 48, priority: "jellyfin");
        var snapshot = Builders.Snapshot(maintenance: [Builders.Upcoming(TimeSpan.FromHours(30), "Jellyfin")]);
        var dto = await Service(Returning(snapshot)).GetAsync(CancellationToken.None);
        Assert.Equal("maintenance_soon", dto.State);
        Assert.True(dto.Maintenance.Upcoming[0].Priority);
    }

    [Fact]
    public async Task WithoutAPublicAddressThereIsNoLink()
    {
        _settings.Current = Configured(publicUrl: "");
        var good = await Service(Returning(Builders.Snapshot())).GetAsync(CancellationToken.None);
        Assert.Null(good.Portal.Url);
        var failing = new CountingClient(_ => Task.FromResult(Failure(SummaryFailure.Timeout, _clock.GetUtcNow())));
        Assert.Null((await Service(failing).GetAsync(CancellationToken.None)).Portal.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://status.example.com")]
    public async Task TheAddressThePortalIsAskedWithNeverReachesABrowserInAnyState(string publicUrl)
    {
        // The address the plugin uses is often a private one (a machine on the local network). Every signed-in user
        // gets the summary, so it must not be in it, whatever the outcome of the reading.
        const string Private = "http://192.168.0.55:5000";
        _settings.Current = Configured(url: Private, publicUrl: publicUrl);
        var failing = new CountingClient(_ => Task.FromResult(Failure(SummaryFailure.Timeout, _clock.GetUtcNow())));
        var summaries = new[]
        {
            await Service(Returning(Builders.Snapshot([Builders.Svc("Jellyfin", ServiceStatus.Down)]))).GetAsync(CancellationToken.None),
            await Service(failing).GetAsync(CancellationToken.None),
        };
        _settings.Current = Configured(url: Private, publicUrl: publicUrl, key: "");
        var unconfigured = await Service(Returning(Builders.Snapshot())).GetAsync(CancellationToken.None);
        foreach (var dto in summaries.Append(unconfigured))
        {
            var json = System.Text.Json.JsonSerializer.Serialize(dto);
            Assert.DoesNotContain("192.168.0.55", json, StringComparison.Ordinal);
            Assert.DoesNotContain(":5000", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ThePollingIntervalIsPassedOnToTheBrowserWhateverTheState()
    {
        _settings.Current = JellyPortalSettings.From(Url, PublicUrl, "k", 45, 30, 24, "", false);
        var good = await Service(Returning(Builders.Snapshot())).GetAsync(CancellationToken.None);
        Assert.Equal(45, good.PollSeconds);
        var failing = new CountingClient(_ => Task.FromResult(Failure(SummaryFailure.Timeout, _clock.GetUtcNow())));
        Assert.Equal(45, (await Service(failing).GetAsync(CancellationToken.None)).PollSeconds);
        _settings.Current = JellyPortalSettings.From("", "", "", 45, 30, 24, "", false);
        Assert.Equal(45, (await Service(Returning(Builders.Snapshot())).GetAsync(CancellationToken.None)).PollSeconds);
    }
}
