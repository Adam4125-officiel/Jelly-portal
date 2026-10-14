using System.Text.Json;
using Jellyfin.Plugin.JellyPortal.Configuration;
using Jellyfin.Plugin.JellyPortal.Injection;
using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;
using Jellyfin.Plugin.JellyPortal.Tests.Summary;
using static Jellyfin.Plugin.JellyPortal.Tests.Portal.Settings;

namespace Jellyfin.Plugin.JellyPortal.Tests.Portal;

public class ConfigServiceTests
{
    private static PluginConfiguration Saved() => new()
    {
        PortalUrl = "http://portal.example:5000",
        PublicUrl = "https://status.example.com",
        ApiKey = "super-secret-key",
        PollSeconds = 45,
        CacheSeconds = 20,
        SoonWindowHours = 12,
        PriorityServices = "Jellyfin\nSeerr",
        AdminsOnly = true,
    };

    // ---- what the page is shown -------------------------------------------------------------------------

    [Fact]
    public void TheViewNeverContainsTheKeyOnlyWhetherOneIsSaved()
    {
        var view = ConfigService.View(Saved(), RegistrationResult.Registered);
        var json = JsonSerializer.Serialize(view);
        Assert.DoesNotContain("super-secret-key", json, StringComparison.Ordinal);
        Assert.True(view.HasApiKey);
        Assert.False(ConfigService.View(new PluginConfiguration(), RegistrationResult.Pending).HasApiKey);
    }

    [Fact]
    public void TheViewCarriesTheSettingsAndTheRangesTheInputsUse()
    {
        var view = ConfigService.View(Saved(), RegistrationResult.Registered);
        Assert.Equal("http://portal.example:5000", view.PortalUrl);
        Assert.Equal(45, view.PollSeconds);
        Assert.Equal(20, view.CacheSeconds);
        Assert.Equal(12, view.SoonWindowHours);
        Assert.Equal("Jellyfin\nSeerr", view.PriorityServices);
        Assert.True(view.AdminsOnly);
        Assert.Equal(new SettingRange(60, 10, 3600), view.Ranges["pollSeconds"]);
        Assert.Equal(new SettingRange(30, 5, 600), view.Ranges["cacheSeconds"]);
        Assert.Equal(new SettingRange(24, 1, 720), view.Ranges["soonWindowHours"]);
    }

    [Theory]
    [InlineData(RegistrationResult.Registered, "registered")]
    [InlineData(RegistrationResult.NotInstalled, "not_installed")]
    [InlineData(RegistrationResult.Incompatible, "incompatible")]
    [InlineData(RegistrationResult.Failed, "failed")]
    [InlineData(RegistrationResult.Pending, "pending")]
    public void TheViewSaysHowFileTransformationWent(RegistrationResult result, string code)
    {
        Assert.Equal(code, ConfigService.View(new PluginConfiguration(), result).FileTransformation);
    }

    // ---- what the page submits --------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankKeyKeepsTheSavedOneBecauseThePageNeverHadIt(string? typed)
    {
        var result = ConfigService.Apply(Saved(), new ConfigUpdate { ApiKey = typed, PollSeconds = 100 });
        Assert.Equal("super-secret-key", result.Configuration!.ApiKey);
        Assert.Equal(100, result.Configuration.PollSeconds);
    }

    [Fact]
    public void ANewKeyReplacesItTrimmed()
    {
        Assert.Equal("new-key", ConfigService.Apply(Saved(), new ConfigUpdate { ApiKey = "  new-key \n" }).Configuration!.ApiKey);
    }

    [Fact]
    public void ClearingErasesTheKeyEvenIfSomethingWasAlsoTyped()
    {
        Assert.Equal(string.Empty, ConfigService.Apply(Saved(), new ConfigUpdate { ClearApiKey = true, ApiKey = "typed" }).Configuration!.ApiKey);
    }

    [Fact]
    public void AFieldLeftOutKeepsItsValue()
    {
        var kept = ConfigService.Apply(Saved(), new ConfigUpdate()).Configuration!;
        Assert.Equal("http://portal.example:5000", kept.PortalUrl);
        Assert.Equal(45, kept.PollSeconds);
        Assert.Equal(20, kept.CacheSeconds);
        Assert.Equal(12, kept.SoonWindowHours);
        Assert.True(kept.AdminsOnly);
        Assert.Equal("Jellyfin\nSeerr", kept.PriorityServices);
    }

    [Fact]
    public void NumbersAreClampedNotRefusedAndTheViewSaysWhatWasKept()
    {
        var result = ConfigService.Apply(Saved(), new ConfigUpdate { PollSeconds = 1, CacheSeconds = 0, SoonWindowHours = 99999 });
        Assert.Equal(10, result.Configuration!.PollSeconds);
        Assert.Equal(5, result.Configuration.CacheSeconds);
        Assert.Equal(720, result.Configuration.SoonWindowHours);
    }

    [Theory]
    [InlineData("portal.example")]
    [InlineData("ftp://portal.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://user:pass@portal.example")]
    [InlineData("http://portal.example/?x=1")]
    public void AnAddressThatCannotBeUsedIsRefusedInFrenchAndNothingIsApplied(string url)
    {
        var result = ConfigService.Apply(Saved(), new ConfigUpdate { PortalUrl = url });
        Assert.Null(result.Configuration);
        Assert.Contains("http://", result.Error, StringComparison.Ordinal);
        Assert.Contains("adresse", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePublicAddressIsShownApartFromTheOneThePortalIsAskedWith()
    {
        var view = ConfigService.View(Saved(), RegistrationResult.Registered);
        Assert.Equal("https://status.example.com", view.PublicUrl);
        Assert.Equal("http://portal.example:5000", view.PortalUrl);
    }

    [Fact]
    public void ThePublicAddressIsKeptWhenNotSubmittedAndRemovedWhenBlank()
    {
        Assert.Equal("https://status.example.com", ConfigService.Apply(Saved(), new ConfigUpdate()).Configuration!.PublicUrl);
        Assert.Equal(string.Empty, ConfigService.Apply(Saved(), new ConfigUpdate { PublicUrl = "  " }).Configuration!.PublicUrl);
        var changed = ConfigService.Apply(Saved(), new ConfigUpdate { PublicUrl = " https://other.example/status " }).Configuration!;
        Assert.Equal("https://other.example/status", changed.PublicUrl);
        Assert.Equal("http://portal.example:5000", changed.PortalUrl);
    }

    [Theory]
    [InlineData("status.example.com")]
    [InlineData("ftp://status.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@status.example.com")]
    [InlineData("https://status.example.com/?x=1")]
    public void APublicAddressThatCannotBeUsedIsRefusedAndNothingIsApplied(string url)
    {
        var result = ConfigService.Apply(Saved(), new ConfigUpdate { PublicUrl = url, PollSeconds = 99 });
        Assert.Null(result.Configuration);
        Assert.Contains("publique", result.Error, StringComparison.Ordinal);
        Assert.Contains("https://", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankAddressIsAllowedItSimplyMeansNotConfigured()
    {
        var result = ConfigService.Apply(Saved(), new ConfigUpdate { PortalUrl = "  " });
        Assert.Equal(string.Empty, result.Configuration!.PortalUrl);
        Assert.False(result.Configuration.ToSettings().IsConfigured);
    }

    [Fact]
    public void ThePriorityListIsNormalisedAndTheVisibilityIsApplied()
    {
        var result = ConfigService.Apply(Saved(), new ConfigUpdate { PriorityServices = " NAS \r\n\r\nnas\nPi-hole ", AdminsOnly = false });
        Assert.Equal("NAS\nPi-hole", result.Configuration!.PriorityServices);
        Assert.False(result.Configuration.AdminsOnly);
    }

    [Fact]
    public void ApplyingNeverModifiesTheStoredConfiguration()
    {
        var saved = Saved();
        ConfigService.Apply(saved, new ConfigUpdate { PortalUrl = "http://other:1", ApiKey = "x", PollSeconds = 999, ClearApiKey = true });
        Assert.Equal("http://portal.example:5000", saved.PortalUrl);
        Assert.Equal("super-secret-key", saved.ApiKey);
        Assert.Equal(45, saved.PollSeconds);
    }

    [Fact]
    public void TheSubmissionIsReadFromTheJsonThePageSends()
    {
        var update = JsonSerializer.Deserialize<ConfigUpdate>("""
            {"portalUrl":"http://p:1","apiKey":null,"clearApiKey":false,"adminsOnly":true,"priorityServices":"A\nB","soonWindowHours":6,"pollSeconds":null,"cacheSeconds":15}
            """)!;
        Assert.Equal("http://p:1", update.PortalUrl);
        Assert.Null(update.ApiKey);
        Assert.True(update.AdminsOnly);
        Assert.Equal(6, update.SoonWindowHours);
        Assert.Null(update.PollSeconds);
        Assert.Equal(15, update.CacheSeconds);
    }
}

public class ConnectionTesterTests
{
    private static readonly ManualTime Clock = new(Builders.Now);

    private static Task<ConnectionResult> Test(CountingClient client, JellyPortalSettings? settings = null) =>
        ConnectionTester.TestAsync(client, settings ?? Configured(), CancellationToken.None);

    [Fact]
    public async Task ASuccessNamesThePortalAndCountsItsServices()
    {
        var snapshot = Builders.Snapshot([Builders.Svc("A"), Builders.Svc("B"), Builders.Svc("C")]);
        var result = await Test(new CountingClient(_ => Task.FromResult(Reading(snapshot, Clock.GetUtcNow()))));
        Assert.True(result.Ok);
        Assert.Null(result.Reason);
        Assert.Equal(3, result.Services);
        Assert.Contains("Home", result.Message, StringComparison.Ordinal);
        Assert.Contains("3 services", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneServiceIsNotPlural()
    {
        var result = await Test(new CountingClient(_ => Task.FromResult(Reading(Builders.Snapshot([Builders.Svc("A")]), Clock.GetUtcNow()))));
        Assert.Contains("1 service.", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SummaryFailure.NotConfigured)]
    [InlineData(SummaryFailure.DeviceApiDisabled)]
    [InlineData(SummaryFailure.BadApiKey)]
    [InlineData(SummaryFailure.Unreachable)]
    [InlineData(SummaryFailure.Timeout)]
    [InlineData(SummaryFailure.BadResponse)]
    [InlineData(SummaryFailure.PortalOutdated)]
    public async Task EachFailureGivesItsReasonCodeAndASentenceThatSaysWhatToDo(SummaryFailure failure)
    {
        var result = await Test(new CountingClient(_ => Task.FromResult(Failure(failure, Clock.GetUtcNow()))));
        Assert.False(result.Ok);
        Assert.Equal(failure.Name(), result.Reason);
        Assert.True(result.Message.Length > 30);
        Assert.DoesNotContain("k-secret", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFailureHasADifferentExplanation()
    {
        var texts = Enum.GetValues<SummaryFailure>().Select(ConnectionTester.Explain).ToList();
        Assert.Equal(texts.Count, texts.Distinct().Count());
    }

    [Fact]
    public void TheExplanationsPointToTheRightPlaceInThePortal()
    {
        Assert.Contains("Display device", ConnectionTester.Explain(SummaryFailure.DeviceApiDisabled), StringComparison.Ordinal);
        Assert.Contains("Display device", ConnectionTester.Explain(SummaryFailure.BadApiKey), StringComparison.Ordinal);
        Assert.Contains("1.12.0", ConnectionTester.Explain(SummaryFailure.PortalOutdated), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItAsksThePortalWithTheSettingsItWasGivenNotTheSavedOnes()
    {
        JellyPortalSettings? seen = null;
        var client = new CountingClient(s =>
        {
            seen = s;
            return Task.FromResult(Failure(SummaryFailure.Timeout, Clock.GetUtcNow()));
        });
        await Test(client, Configured(url: "http://typed.example:1", key: "typed-key"));
        Assert.Equal("http://typed.example:1", seen!.PortalUrl);
        Assert.Equal("typed-key", seen.ApiKey);
    }
}

public class SettingsPageTests
{
    private static string Page()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.JellyPortal.Configuration.configPage.html")
            ?? throw new InvalidOperationException("the settings page is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void ThePageIsEmbeddedAndSpeaksFrench()
    {
        var page = Page();
        Assert.Contains("Enregistrer", page, StringComparison.Ordinal);
        Assert.Contains("Tester la connexion", page, StringComparison.Ordinal);
        Assert.Contains("Services prioritaires", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageNeverUsesJellyfinsGenericConfigurationApiWhichWouldHandItTheKeyOrWipeIt()
    {
        var page = Page();
        Assert.DoesNotContain("getPluginConfiguration", page, StringComparison.Ordinal);
        Assert.DoesNotContain("updatePluginConfiguration", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Plugins/", page, StringComparison.Ordinal);
        Assert.Contains("JellyPortal/' + path", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageWritesTextAndNeverHtml()
    {
        // Messages come from the server (and the portal's name from the portal): textContent, never innerHTML.
        var page = Page();
        Assert.DoesNotContain("innerHTML", page, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", page, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", page, StringComparison.Ordinal);
        Assert.Contains("textContent", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageHasEveryFieldTheScriptReads()
    {
        var page = Page();
        foreach (var id in new[] { "jpPortalUrl", "jpPublicUrl", "jpApiKey", "jpClearKey", "jpTest", "jpTestResult", "jpAdminsOnly", "jpPriority", "jpSoon", "jpPoll", "jpCache", "jpSaveResult", "jpFileTransformation", "jpForm" })
        {
            Assert.Contains($"id=\"{id}\"", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheKeyFieldIsAPasswordFieldThatBrowsersDoNotAutofill()
    {
        var page = Page();
        Assert.Contains("id=\"jpApiKey\" name=\"apiKey\" type=\"password\"", page, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"new-password\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginDeclaresThePageUnderItsOwnName()
    {
        // The dashboard links a plugin to the configuration page by this name.
        // GetPages() uses no state, so the constructor (which wants the server's paths and serializer) is not run.
        var plugin = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        var pages = plugin.GetPages().ToList();
        var page = Assert.Single(pages);
        Assert.Equal("Jelly Portal", page.Name);
        Assert.Equal("Jellyfin.Plugin.JellyPortal.Configuration.configPage.html", page.EmbeddedResourcePath);
        // Without this the settings are only reachable through Plugins > Jelly Portal.
        Assert.True(page.EnableInMainMenu);
        Assert.Equal("Jelly Portal", page.DisplayName);
    }
}
