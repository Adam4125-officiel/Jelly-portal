using Jellyfin.Plugin.JellyPortal.Api;
using Jellyfin.Plugin.JellyPortal.Injection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.JellyPortal.Tests.Injection;

public class IndexHtmlInjectorTests
{
    private const string Page = "<!doctype html><html><head><title>Jellyfin</title></head><body><div id=\"reactRoot\"></div><script src=\"main.js\"></script></body></html>";

    [Fact]
    public void TheStylesheetGoesBeforeTheEndOfTheHeadAndTheScriptBeforeTheEndOfTheBody()
    {
        var result = IndexHtmlInjector.Inject(Page, "1.2.3.4");
        Assert.Contains("<link rel=\"stylesheet\" href=\"../JellyPortal/client.css?v=1.2.3.4\" data-jellyportal></head>", result, StringComparison.Ordinal);
        Assert.Contains("<script defer src=\"../JellyPortal/client.js?v=1.2.3.4\" data-jellyportal></script></body>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void EverythingElseInThePageIsLeftExactlyAsItWas()
    {
        var result = IndexHtmlInjector.Inject(Page, "1");
        var withoutOurs = result
            .Replace("<link rel=\"stylesheet\" href=\"../JellyPortal/client.css?v=1\" data-jellyportal>", string.Empty, StringComparison.Ordinal)
            .Replace("<script defer src=\"../JellyPortal/client.js?v=1\" data-jellyportal></script>", string.Empty, StringComparison.Ordinal);
        Assert.Equal(Page, withoutOurs);
    }

    [Fact]
    public void ItIsIdempotent()
    {
        var once = IndexHtmlInjector.Inject(Page, "1");
        Assert.Equal(once, IndexHtmlInjector.Inject(once, "1"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(once, "client\\.js"));
    }

    [Fact]
    public void TagsAreFoundWhateverTheirCase()
    {
        var upper = Page.Replace("</head>", "</HEAD>", StringComparison.Ordinal).Replace("</body>", "</BODY>", StringComparison.Ordinal);
        var result = IndexHtmlInjector.Inject(upper, "1");
        Assert.Contains("data-jellyportal></HEAD>", result, StringComparison.Ordinal);
        Assert.Contains("data-jellyportal></script></BODY>", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just text")]
    [InlineData("<html><head></head>")]
    [InlineData("<html><body></body></html>")]
    [InlineData("</body></head>")]
    public void APageItCannotAnchorOnIsReturnedUntouched(string html)
    {
        Assert.Equal(html, IndexHtmlInjector.Inject(html, "1"));
    }

    [Fact]
    public void TheAddressesAreRelativeSoABaseUrlCannotBreakThem()
    {
        var result = IndexHtmlInjector.Inject(Page, "1");
        Assert.DoesNotContain("href=\"/", result, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"/JellyPortal", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVersionCannotBreakOutOfTheAttribute()
    {
        var result = IndexHtmlInjector.Inject(Page, "1\"><script>alert(1)</script>");
        Assert.DoesNotContain("<script>alert", result, StringComparison.Ordinal);
    }
}

public class FileTransformationBridgeTests
{
    private sealed class Capture : ILogger<FileTransformationBridge>
    {
        public List<(LogLevel Level, string Message, Exception? Error)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception), exception));
    }

    private readonly Capture _log = new();

    private RegistrationResult Register(params System.Reflection.Assembly[] assemblies) => new FileTransformationBridge(_log).Register(assemblies);

    [Fact]
    public void WithFileTransformationInstalledTheTransformationIsRegistered()
    {
        Jellyfin.Plugin.FileTransformation.PluginInterface.LastPayload = null;
        Assert.Equal(RegistrationResult.Registered, Register(FakeFileTransformation.Working));
        var payload = Jellyfin.Plugin.FileTransformation.PluginInterface.LastPayload!;
        Assert.Equal("1f18e5b5-a6f3-4f8c-8e23-6fbff7e9b1ca", (string)payload["id"]!);
        Assert.Equal("index.html", (string)payload["fileNamePattern"]!);
        Assert.Equal(typeof(IndexHtmlTransformation).Assembly.FullName, (string)payload["callbackAssembly"]!);
        Assert.Equal("Jellyfin.Plugin.JellyPortal.Injection.IndexHtmlTransformation", (string)payload["callbackClass"]!);
        Assert.Equal("Apply", (string)payload["callbackMethod"]!);
        Assert.Contains(_log.Lines, l => l.Level == LogLevel.Information && l.Message.StartsWith("[JellyPortal]", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRegistrationUsesTheSameKeyAsTheOtherPluginsThatPatchIndexHtml()
    {
        // File Transformation keeps one list per exact key and, for index.html, runs only the list under the key
        // "index.html" when there is one. A different pattern string would not be run, or would stop the others.
        Assert.Equal("index.html", IndexHtmlTransformation.FilePattern);
    }

    [Fact]
    public void FileTransformationCallsOurCallbackTheWayItReallyDoes()
    {
        Jellyfin.Plugin.FileTransformation.PluginInterface.LastPayload = null;
        Register(FakeFileTransformation.Working);
        var html = "<html><head></head><body></body></html>";
        var result = FakeFileTransformation.Invoke(Jellyfin.Plugin.FileTransformation.PluginInterface.LastPayload!, html);
        Assert.NotNull(result);
        Assert.Contains("../JellyPortal/client.js?v=", result, StringComparison.Ordinal);
        Assert.Contains("../JellyPortal/client.css?v=", result, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutFileTransformationItWarnsClearlyAndDoesNotThrow()
    {
        Assert.Equal(RegistrationResult.NotInstalled, Register());
        var line = Assert.Single(_log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.StartsWith("[JellyPortal]", line.Message, StringComparison.Ordinal);
        Assert.Contains("File Transformation", line.Message, StringComparison.Ordinal);
        Assert.Contains("restart", line.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnrelatedAssembliesAreIgnored()
    {
        Assert.Equal(RegistrationResult.NotInstalled, Register(typeof(string).Assembly, typeof(Uri).Assembly));
    }

    [Theory]
    [InlineData("no-method")]
    [InlineData("two-parameters")]
    [InlineData("unparseable-parameter")]
    public void AFileTransformationThatOffersADifferentRegistrationIsReportedAsIncompatible(string variant)
    {
        Assert.Equal(RegistrationResult.Incompatible, Register(FakeFileTransformation.Shaped(variant)));
        var line = Assert.Single(_log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.StartsWith("[JellyPortal]", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileTransformationThatThrowsDoesNotTakeJellyfinDown()
    {
        Assert.Equal(RegistrationResult.Failed, Register(FakeFileTransformation.Shaped("throws")));
        var line = Assert.Single(_log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal("File Transformation is broken", line.Error!.Message);   // the real cause, not the reflection wrapper
    }

    [Fact]
    public async Task TheRegistrarRecordsTheOutcomeForTheSettingsPage()
    {
        var status = new InjectionStatus();
        Assert.Equal(RegistrationResult.Pending, status.Result);
        var registrar = new FileTransformationRegistrar(new FileTransformationBridge(NullLogger<FileTransformationBridge>.Instance), status);
        await registrar.StartAsync(CancellationToken.None);
        Assert.True(status.Result is RegistrationResult.NotInstalled or RegistrationResult.Registered);
    }
}

public class ClientAssetsTests
{
    [Fact]
    public void TheScriptAndTheStylesheetAreEmbedded()
    {
        var js = ClientAssets.Find("client.js")!;
        var css = ClientAssets.Find("client.css")!;
        Assert.NotEmpty(js.Bytes);
        Assert.NotEmpty(css.Bytes);
        Assert.StartsWith("text/javascript", js.ContentType, StringComparison.Ordinal);
        Assert.StartsWith("text/css", css.ContentType, StringComparison.Ordinal);
    }

    [Fact]
    public void TheValidatorIsStableAndQuoted()
    {
        var first = ClientAssets.Find("client.js")!.ETag;
        Assert.Equal(first, ClientAssets.Find("client.js")!.ETag);
        Assert.StartsWith("\"", first, StringComparison.Ordinal);
        Assert.EndsWith("\"", first, StringComparison.Ordinal);
        Assert.NotEqual(first, ClientAssets.Find("client.css")!.ETag);
    }

    [Theory]
    [InlineData("../client.js")]
    [InlineData("client.js/../../etc/passwd")]
    [InlineData("Client.js")]
    [InlineData("")]
    [InlineData("jellyportal.js")]
    [InlineData("client.map")]
    public void OnlyTheTwoKnownNamesAreServedNothingIsEverJoinedToAPath(string name)
    {
        Assert.Null(ClientAssets.Find(name));
    }
}
