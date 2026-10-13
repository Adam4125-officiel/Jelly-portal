using System.Text.Json;
using System.Xml.Serialization;
using Jellyfin.Plugin.JellyPortal.Configuration;
using Jellyfin.Plugin.JellyPortal.Portal;

namespace Jellyfin.Plugin.JellyPortal.Tests.Portal;

public class PluginConfigurationTests
{
    [Fact]
    public void TheDefaultsAreTheDocumentedOnes()
    {
        var s = new PluginConfiguration().ToSettings();
        Assert.Equal(60, s.PollSeconds);
        Assert.Equal(30, s.CacheSeconds);
        Assert.Equal(24, s.SoonWindowHours);
        Assert.False(s.AdminsOnly);
        Assert.False(s.IsConfigured);
        Assert.Empty(s.PriorityServices);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(60, 60)]
    [InlineData(999999, 3600)]
    public void PollingIsKeptBetweenTenSecondsAndOneHour(int configured, int expected)
    {
        Assert.Equal(expected, new PluginConfiguration { PollSeconds = configured }.ToSettings().PollSeconds);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(0, 5)]
    [InlineData(30, 30)]
    [InlineData(999999, 600)]
    public void TheCacheIsNeverBelowFiveSecondsSoBrowsersCannotBecomeRequestsToThePortal(int configured, int expected)
    {
        Assert.Equal(expected, new PluginConfiguration { CacheSeconds = configured }.ToSettings().CacheSeconds);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(24, 24)]
    [InlineData(100000, 720)]
    public void TheWindowIsKeptBetweenOneHourAndThirtyDays(int configured, int expected)
    {
        Assert.Equal(expected, new PluginConfiguration { SoonWindowHours = configured }.ToSettings().SoonWindowHours);
    }

    [Fact]
    public void AddressAndKeyAreTrimmedAndBothAreNeededToBeConfigured()
    {
        Assert.True(new PluginConfiguration { PortalUrl = "  http://p:5000 ", ApiKey = " k " }.ToSettings().IsConfigured);
        Assert.Equal("http://p:5000", new PluginConfiguration { PortalUrl = "  http://p:5000 " }.ToSettings().PortalUrl);
        Assert.False(new PluginConfiguration { PortalUrl = "http://p:5000" }.ToSettings().IsConfigured);
        Assert.False(new PluginConfiguration { ApiKey = "k" }.ToSettings().IsConfigured);
    }

    [Fact]
    public void ThePriorityListIsParsedFromOneNamePerLine()
    {
        var s = new PluginConfiguration { PriorityServices = "Jellyfin\r\n seerr \n\nJELLYFIN" }.ToSettings();
        Assert.Equal(["Jellyfin", "seerr"], s.PriorityServices);
    }

    [Fact]
    public void TheKeyNeverAppearsInAnyJsonAnswer()
    {
        // GET /Plugins/{id}/Configuration serialises this object; the settings page must never be handed the key.
        var json = JsonSerializer.Serialize(new PluginConfiguration { ApiKey = "super-secret-key", PortalUrl = "http://p" });
        Assert.DoesNotContain("super-secret-key", json);
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PortalUrl", json);
    }

    [Fact]
    public void TheKeyIsStillKeptByTheXmlStoreJellyfinUses()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, new PluginConfiguration { ApiKey = "super-secret-key", AdminsOnly = true });
        using var reader = new StringReader(writer.ToString());
        var back = (PluginConfiguration)serializer.Deserialize(reader)!;
        Assert.Equal("super-secret-key", back.ApiKey);
        Assert.True(back.AdminsOnly);
    }
}
