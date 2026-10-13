using System.Net;
using Jellyfin.Plugin.JellyPortal.Portal;
using Jellyfin.Plugin.JellyPortal.Summary;
using Jellyfin.Plugin.JellyPortal.Tests.Summary;
using static Jellyfin.Plugin.JellyPortal.Tests.Portal.Settings;

namespace Jellyfin.Plugin.JellyPortal.Tests.Portal;

public class PortalClientTests
{
    private static readonly ManualTime Clock = new(Builders.Now);

    private static async Task<(PortalReading Reading, StubHandler Handler)> Read(HttpStatusCode status, string body = "", string url = Url)
    {
        var handler = new StubHandler(status, body);
        using var client = Client(handler, Clock);
        return (await client.ReadAsync(Configured(url: url), CancellationToken.None), handler);
    }

    // ---- exactly what is asked --------------------------------------------------------------------------

    [Fact]
    public async Task TheRequestIsExactlyTheOneThePlanDocuments()
    {
        var (_, handler) = await Read(HttpStatusCode.OK, DevicePayloadParserTests.Valid);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "http://portal.example:5000/api/device/summary?sections=services,incidents,maintenance,announcements&services=all&detail=full",
            request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("resources")]
    [InlineData("vms")]
    [InlineData("jellyfin")]
    public async Task ItNeverAsksForTheInsideOfTheServer(string forbidden)
    {
        // The device API ignores the portal's public-visibility switches and this plugin shows its answer
        // to every Jellyfin user: asking for these would hand the machine's internals to all of them.
        var (_, handler) = await Read(HttpStatusCode.OK, DevicePayloadParserTests.Valid);
        Assert.DoesNotContain(forbidden, handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task TheKeyTravelsInTheHeaderOnlyNeverInTheAddress()
    {
        var (_, handler) = await Read(HttpStatusCode.OK, DevicePayloadParserTests.Valid);
        var request = handler.Requests[0];
        Assert.Equal(["k-secret"], request.Headers.GetValues("X-Api-Key"));
        Assert.DoesNotContain("k-secret", request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("http://portal.example:5000", "http://portal.example:5000/api/device/summary")]
    [InlineData("http://portal.example:5000/", "http://portal.example:5000/api/device/summary")]
    [InlineData("  https://portal.example/  ", "https://portal.example/api/device/summary")]
    [InlineData("https://home.example/status", "https://home.example/status/api/device/summary")]
    [InlineData("https://home.example/status/", "https://home.example/status/api/device/summary")]
    public void ThePathPrefixThePortalIsServedUnderIsKept(string configured, string expectedStart)
    {
        Assert.True(PortalClient.TryBuildUri(configured, out var uri));
        Assert.StartsWith(expectedStart + "?", uri.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("portal.example")]
    [InlineData("ftp://portal.example")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://user:pass@portal.example")]
    [InlineData("http://portal.example/?x=1")]
    [InlineData("http://portal.example/#frag")]
    public void AnAddressThatIsNotAPlainHttpAddressIsNotUsed(string configured)
    {
        Assert.False(PortalClient.TryBuildUri(configured, out _));
    }

    [Theory]
    [InlineData("ftp://portal.example", "k")]
    [InlineData("", "k")]
    [InlineData(Url, "")]
    public async Task WithoutAUsableAddressAndKeyNothingIsSent(string url, string key)
    {
        var handler = new StubHandler(HttpStatusCode.OK, DevicePayloadParserTests.Valid);
        using var client = Client(handler, Clock);
        var reading = await client.ReadAsync(JellyPortalSettings.From(url, key, 60, 30, 24, "", false), CancellationToken.None);
        Assert.Equal(SummaryFailure.NotConfigured, reading.Outcome.Failure);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void RedirectsAreNotFollowedSoTheKeyCannotBeForwardedToAnotherHost()
    {
        using var handler = (SocketsHttpHandler)PortalClient.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
    }

    // ---- what comes back --------------------------------------------------------------------------------

    [Fact]
    public async Task AGoodAnswerIsParsedAndStampedWithTheTimeItArrived()
    {
        var (reading, _) = await Read(HttpStatusCode.OK, DevicePayloadParserTests.Valid);
        Assert.NotNull(reading.Outcome.Snapshot);
        Assert.Equal(5, reading.Outcome.Snapshot!.Services.Count);
        Assert.Equal(Builders.Now, reading.ReadAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, SummaryFailure.DeviceApiDisabled)]
    [InlineData(HttpStatusCode.Unauthorized, SummaryFailure.BadApiKey)]
    [InlineData(HttpStatusCode.BadGateway, SummaryFailure.Unreachable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SummaryFailure.Unreachable)]
    [InlineData(HttpStatusCode.GatewayTimeout, SummaryFailure.Unreachable)]
    [InlineData(HttpStatusCode.InternalServerError, SummaryFailure.BadResponse)]
    [InlineData(HttpStatusCode.Forbidden, SummaryFailure.BadResponse)]
    [InlineData(HttpStatusCode.Found, SummaryFailure.BadResponse)]
    [InlineData(HttpStatusCode.MovedPermanently, SummaryFailure.BadResponse)]
    [InlineData(HttpStatusCode.BadRequest, SummaryFailure.BadResponse)]
    public async Task EachKindOfRefusalHasItsOwnReason(HttpStatusCode status, SummaryFailure expected)
    {
        var (reading, _) = await Read(status, "{\"error\":\"nope\"}");
        Assert.Equal(expected, reading.Outcome.Failure);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("<html>maintenance page</html>")]
    [InlineData("{}")]
    public async Task A200WithSomethingElseIsABadResponse(string body)
    {
        var (reading, _) = await Read(HttpStatusCode.OK, body);
        Assert.Equal(SummaryFailure.BadResponse, reading.Outcome.Failure);
    }

    [Fact]
    public async Task AnOlderPortalThatIgnoredDetailFullIsReportedAsOutdated()
    {
        var compact = DevicePayloadParserTests.Valid.Replace("\"detail\":\"full\",", string.Empty, StringComparison.Ordinal);
        var (reading, _) = await Read(HttpStatusCode.OK, compact);
        Assert.Equal(SummaryFailure.PortalOutdated, reading.Outcome.Failure);
    }

    [Fact]
    public async Task AnswerLargerThanTheLimitIsRefusedWithoutBeingParsed()
    {
        var (reading, _) = await Read(HttpStatusCode.OK, new string(' ', PortalClient.MaxBodyBytes + 1));
        Assert.Equal(SummaryFailure.BadResponse, reading.Outcome.Failure);
    }

    [Fact]
    public async Task ARefusedConnectionIsUnreachable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Connection refused"));
        using var client = Client(handler, Clock);
        var reading = await client.ReadAsync(Configured(), CancellationToken.None);
        Assert.Equal(SummaryFailure.Unreachable, reading.Outcome.Failure);
    }

    [Fact]
    public async Task APortalThatDoesNotAnswerInTimeIsATimeout()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = Client(handler, Clock, TimeSpan.FromMilliseconds(80));
        var reading = await client.ReadAsync(Configured(), CancellationToken.None);
        Assert.Equal(SummaryFailure.Timeout, reading.Outcome.Failure);
    }

    [Fact]
    public async Task AnAnswerThatStallsHalfWayIsATimeoutToo()
    {
        var handler = new StubHandler((_, token) =>
        {
            var stalling = new StreamContent(new StallingStream(token));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = stalling });
        });
        using var client = Client(handler, Clock, TimeSpan.FromMilliseconds(80));
        var reading = await client.ReadAsync(Configured(), CancellationToken.None);
        Assert.Equal(SummaryFailure.Timeout, reading.Outcome.Failure);
    }

    [Fact]
    public async Task AUserWhoGivesUpIsNotATimeout()
    {
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = Client(handler, Clock);
        using var gone = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadAsync(Configured(), gone.Token));
    }

    /// <summary>A body that sends a byte and then never anything else.</summary>
    private sealed class StallingStream : Stream
    {
        private readonly CancellationToken _token;
        private bool _sent;

        public StallingStream(CancellationToken token) => _token = token;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                buffer.Span[0] = (byte)'{';
                return 1;
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
