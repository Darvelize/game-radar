using System.Net;
using System.Text;
using GameRadar.Core.Updates;

using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Updates;

public class UpdateCheckerTests
{
    private static readonly Version Current = new(1, 0, 0, 0);

    // ---------- Parsing ----------

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("V1.10.3", "1.10.3")]
    [InlineData(" v2.0 ", "2.0")]
    public void ParseTag_ReadsTheVersion(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseTag(tag));
    }

    [Theory]
    [InlineData("v1.2.0-beta")]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData("")]
    public void ParseTag_IgnoresUnexpectedTags(string tag)
    {
        Assert.Null(UpdateChecker.ParseTag(tag));
    }

    [Fact]
    public void ParseLatestVersion_ReadsTheTagOfTheRelease()
    {
        var version = UpdateChecker.ParseLatestVersion("""
            { "tag_name": "v1.1.0", "name": "GameRadar 1.1.0", "draft": false, "prerelease": false }
            """);

        Assert.Equal(new Version(1, 1, 0), version);
    }

    [Fact]
    public void ParseLatestVersion_IsNullWithoutTag()
    {
        Assert.Null(UpdateChecker.ParseLatestVersion("""{ "message": "Not Found" }"""));
    }

    // ---------- Comparison ----------

    [Theory]
    [InlineData("1.0.1", "1.0.0.0", true)]
    [InlineData("1.1.0", "1.0.0.0", true)]
    [InlineData("2.0", "1.9.9.0", true)]
    // Numbers, not text: "1.10" comes after "1.9".
    [InlineData("1.10.0", "1.9.0.0", true)]
    // Same version written with 3 and 4 parts: not an update.
    [InlineData("1.0.0", "1.0.0.0", false)]
    [InlineData("1.0", "1.0.0.0", false)]
    // A friend running a newer local build is not asked to go back.
    [InlineData("1.0.0", "1.1.0.0", false)]
    public void IsNewer_ComparesPartByPart(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(Version.Parse(candidate), Version.Parse(current)));
    }

    // ---------- HTTP ----------

    [Fact]
    public async Task Check_FindsANewerReleaseAndSendsAUserAgent()
    {
        var handler = new FakeGitHubHandler(HttpStatusCode.OK, """{ "tag_name": "v1.1.0" }""");
        using var http = new HttpClient(handler);

        var update = await new UpdateChecker(http).CheckAsync(Current);

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 1, 0), update.Version);
        Assert.Equal(UpdateChecker.DownloadPage, update.DownloadPage);
        // Without a User-Agent, GitHub answers 403.
        Assert.Contains("GameRadar", handler.UserAgent);
    }

    [Fact]
    public async Task Check_IsNullWhenAlreadyUpToDate()
    {
        using var http = new HttpClient(new FakeGitHubHandler(HttpStatusCode.OK, """{ "tag_name": "v1.0.0" }"""));

        Assert.Null(await new UpdateChecker(http).CheckAsync(Current));
    }

    [Theory]
    // No release published yet.
    [InlineData(HttpStatusCode.NotFound, "")]
    // Limit of 60 requests per hour without a GitHub account.
    [InlineData(HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""")]
    // Unexpected answer (captive portal of a hotel Wi-Fi...).
    [InlineData(HttpStatusCode.OK, "<html>Connexion requise</html>")]
    public async Task Check_StaysSilentWhenGitHubCannotAnswer(HttpStatusCode status, string body)
    {
        using var http = new HttpClient(new FakeGitHubHandler(status, body));

        Assert.Null(await new UpdateChecker(http).CheckAsync(Current));
    }

    [Fact]
    public async Task Check_StaysSilentOffline()
    {
        using var http = new HttpClient(new FakeGitHubHandler(offline: true));

        Assert.Null(await new UpdateChecker(http).CheckAsync(Current));
    }

    [Fact]
    public async Task Check_LetsTheCallerCancel()
    {
        using var http = new HttpClient(new FakeGitHubHandler(HttpStatusCode.OK, """{ "tag_name": "v1.1.0" }"""));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateChecker(http).CheckAsync(Current, cts.Token));
    }

    private sealed class FakeGitHubHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "", bool offline = false)
        : HttpMessageHandler
    {
        public string UserAgent { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (offline)
                throw new HttpRequestException("No such host is known.");

            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(Json(body, status));
        }
    }
}
