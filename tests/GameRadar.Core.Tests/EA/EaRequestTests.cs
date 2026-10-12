using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GameRadar.Core.EA;
using GameRadar.Core.Logging;
using GameRadar.Core.Resources;

namespace GameRadar.Core.Tests.EA;

public class EaRequestTests
{
    private const string Token = "QVQxOjIuMDozLjA6NjA6bWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFk";

    [Fact]
    public async Task FetchRecentGamesJsonAsync_PostsTheSlugsWithTheToken()
    {
        var handler = new CapturingHandler("""{"data":{"me":{"recentGames":{"items":[]}}}}""");
        var client = new EaClient(new HttpClient(handler), Token);

        await client.FetchRecentGamesJsonAsync(["anthem", "titanfall-2"]);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://service-aggregation-layer.juno.ea.com/graphql", handler.Uri!.ToString());
        Assert.Equal($"Bearer {Token}", handler.Authorization);
        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Contains("recentGames", body["query"]!.GetValue<string>());
        Assert.Equal(["anthem", "titanfall-2"], body["variables"]!["gameSlugs"]!.AsArray().Select(s => s!.GetValue<string>()));
    }

    [Fact]
    public async Task FetchRecentGamesJsonAsync_RefusesMoreSlugsThanEaReadsAtOnce()
    {
        var client = new EaClient(new HttpClient(new CapturingHandler("{}")), Token);
        string[] tooMany = [.. Enumerable.Range(0, EaClient.RecentGamesBatchSize + 1).Select(i => $"game-{i}")];

        await Assert.ThrowsAsync<ArgumentException>(() => client.FetchRecentGamesJsonAsync(tooMany));
    }

    [Fact]
    public async Task FetchGameSlugsAsync_AsksWithoutTheToken()
    {
        // The list of EA's games is public: the token is not sent where it is not needed.
        var handler = new CapturingHandler("""{"data":{"gameSearch":{"items":[{"slug":"anthem"}]}}}""");
        var client = new EaClient(new HttpClient(handler), Token);

        Assert.Equal(["anthem"], await client.FetchGameSlugsAsync());
        Assert.Null(handler.Authorization);
    }

    [Fact]
    public async Task FetchAchievementsAsync_AsksTheOfferForThePlayer()
    {
        var handler = new CapturingHandler("""{"data":{"achievements":[{"achievements":[{"awardCount":1},{"awardCount":0}]}]}}""");
        var client = new EaClient(new HttpClient(handler), Token);

        var achievements = await client.FetchAchievementsAsync("Origin.OFR.50.0003532", "1000123");

        Assert.Equal(new EaAchievements(1, 2), achievements);
        var variables = JsonNode.Parse(handler.Body!)!["variables"]!;
        Assert.Equal("Origin.OFR.50.0003532", variables["offerId"]!.GetValue<string>());
        Assert.Equal("1000123", variables["playerPsd"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AnExpiredToken_SaysToCopyTheEaPageAgain(HttpStatusCode status)
    {
        // Measured: an unknown or expired token answers 401 with an empty body.
        var client = new EaClient(new HttpClient(new CapturingHandler("", status)), Token);

        var ex = await Assert.ThrowsAsync<EaException>(() => client.FetchPlayerIdAsync());

        Assert.Equal(CoreStrings.Ea_TokenExpired, ex.Message);
        Assert.True(ex.IsTokenRefused);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "Ea_RateLimited")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Ea_Down")]
    public async Task OtherRefusals_AreExplained(HttpStatusCode status, string message)
    {
        var client = new EaClient(new HttpClient(new CapturingHandler("", status)), Token);

        var ex = await Assert.ThrowsAsync<EaException>(() => client.FetchGamesJsonAsync(["anthem"]));

        Assert.Equal(CoreStrings.ResourceManager.GetString(message, CoreStrings.Culture), ex.Message);
    }

    [Fact]
    public void ErrorLog_MasksAnEaToken()
    {
        string redacted = ErrorLog.Redact($"GraphQL failed with Authorization: Bearer {Token} and token={Token}");

        Assert.DoesNotContain("QVQxOjIuMDozLjA6", redacted);
    }

    private sealed class CapturingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
