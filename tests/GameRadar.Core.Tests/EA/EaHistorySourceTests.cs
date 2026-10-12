using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GameRadar.Core.Cache;
using GameRadar.Core.EA;
using GameRadar.Core.Models;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.EA;

public class EaHistorySourceTests
{
    private const string Token = "QVQxOjIuMDozLjA6NjA6bWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFk";

    /// <summary>
    /// A fake EA: 12 games in the catalog, two of them played (Jedi, platinum;
    /// Anthem, a few achievements). Records each request's query and slugs.
    /// </summary>
    private sealed class FakeEa : HttpMessageHandler
    {
        public string JediLastSession { get; set; } = "2023-05-07T15:26:58.000Z";
        /// <summary>Set: Anthem's achievements answer this error.</summary>
        public HttpStatusCode? AnthemAchievementsError { get; set; }
        public List<(string Kind, int Slugs)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            string query = body["query"]!.GetValue<string>();
            var slugs = (body["variables"]?["gameSlugs"] ?? body["variables"]?["slugs"])?.AsArray().Select(s => s!.GetValue<string>()).ToList() ?? [];
            string kind = query.Contains("gameSearch") ? "search" : query.Contains("recentGames") ? "recent"
                : query.Contains("games(") ? "games" : query.Contains("achievements") ? "achievements" : "identity";
            Requests.Add((kind, slugs.Count));
            if (kind == "achievements" && AnthemAchievementsError is { } error
                && body["variables"]!["offerId"]!.GetValue<string>() == "offer-anthem")
                return new HttpResponseMessage(error) { Content = new StringContent("") };
            string json = kind switch
            {
                "search" => """{"data":{"gameSearch":{"items":[""" + string.Join(",", Enumerable.Range(1, 10).Select(i => $$"""{"slug":"game-{{i}}"}""")) + """,{"slug":"anthem"},{"slug":"star-wars-jedi-fallen-order"}]}}}""",
                "recent" => """{"data":{"me":{"recentGames":{"items":[""" + string.Join(",", slugs.Where(s => s is "anthem" or "star-wars-jedi-fallen-order").Select(s => s == "anthem"
                    ? """{"gameSlug":"anthem","lastSessionEndDate":"2021-02-01T20:00:00.000Z","totalPlayTimeSeconds":35640}"""
                    : $$"""{"gameSlug":"star-wars-jedi-fallen-order","lastSessionEndDate":"{{JediLastSession}}","totalPlayTimeSeconds":231120}""")) + "]}}}}",
                "games" => """{"data":{"games":{"items":[""" + string.Join(",", slugs.Select(s => $$$"""{"slug":"{{{s}}}","products":{"items":[{"name":"{{{(s == "anthem" ? "Anthem™" : "STAR WARS Jedi: Fallen Order™")}}}","originOfferId":"offer-{{{s}}}","baseItem":{"gameType":"BASE_GAME"}}]}}""")) + "]}}}",
                "achievements" => body["variables"]!["offerId"]!.GetValue<string>() == "offer-anthem"
                    ? """{"data":{"achievements":[{"achievements":[{"awardCount":1},{"awardCount":0},{"awardCount":0},{"awardCount":0}]}]}}"""
                    : """{"data":{"achievements":[{"achievements":[{"awardCount":1},{"awardCount":1}]}]}}""",
                _ => """{"data":{"me":{"player":{"psd":"1000123"}}}}"""
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task RefreshAsync_ReadsThePlayedGamesWithTheirAchievements()
    {
        using var folder = new TempFolder();
        var ea = new FakeEa();
        var source = new EaHistorySource(new CacheStore(folder.Path));

        var history = await source.RefreshAsync(new EaClient(new HttpClient(ea), Token));

        // Most recently played first.
        Assert.Equal(["STAR WARS Jedi: Fallen Order™", "Anthem™"], history.Games.Select(g => g.Name));
        var jedi = history.Games[0];
        Assert.Equal("EA", jedi.Platform);
        Assert.Equal("star-wars-jedi-fallen-order", jedi.PlatformId);
        Assert.Equal(3852, jedi.PlaytimeMinutes);
        Assert.Equal(2, jedi.UnlockedAchievements);
        Assert.Equal(2, jedi.TotalAchievements);
        Assert.Equal(PlayStatus.Mastered, jedi.Status);
        Assert.Equal(25, history.Games[1].ProgressPercentage);
        Assert.Equal(PlayStatus.Played, history.Games[1].Status);
    }

    [Fact]
    public async Task RefreshAsync_AsksAtMostFiveSlugsAtOnce()
    {
        using var folder = new TempFolder();
        var ea = new FakeEa();

        await new EaHistorySource(new CacheStore(folder.Path)).RefreshAsync(new EaClient(new HttpClient(ea), Token));

        // 12 slugs: 3 recentGames requests, never more than 5 slugs each.
        Assert.Equal([5, 5, 2], ea.Requests.Where(r => r.Kind == "recent").Select(r => r.Slugs));
        Assert.All(ea.Requests.Where(r => r.Kind == "games"), r => Assert.InRange(r.Slugs, 1, EaClient.RecentGamesBatchSize));
    }

    [Fact]
    public async Task LoadCached_ReadsTheSavedHistoryBack()
    {
        using var folder = new TempFolder();
        var source = new EaHistorySource(new CacheStore(folder.Path));
        Assert.Null(source.LoadCached());

        await source.RefreshAsync(new EaClient(new HttpClient(new FakeEa()), Token));

        var cached = new EaHistorySource(new CacheStore(folder.Path)).LoadCached()!;
        Assert.Equal(["STAR WARS Jedi: Fallen Order™", "Anthem™"], cached.Games.Select(g => g.Name));
        Assert.Equal(PlayStatus.Mastered, cached.Games[0].Status);
    }

    [Fact]
    public async Task RefreshAsync_AsksTheAchievementsAgainOnlyForAGamePlayedSince()
    {
        using var folder = new TempFolder();
        var ea = new FakeEa();
        var source = new EaHistorySource(new CacheStore(folder.Path));
        await source.RefreshAsync(new EaClient(new HttpClient(ea), Token));
        ea.Requests.Clear();

        // Nothing played since: no achievement asked again.
        await source.RefreshAsync(new EaClient(new HttpClient(ea), Token));
        Assert.DoesNotContain(ea.Requests, r => r.Kind == "achievements");

        // Jedi played again: its achievements only.
        ea.JediLastSession = "2026-10-01T20:00:00.000Z";
        ea.Requests.Clear();
        await source.RefreshAsync(new EaClient(new HttpClient(ea), Token));
        Assert.Single(ea.Requests, r => r.Kind == "achievements");
    }

    [Fact]
    public async Task RefreshAsync_AnExpiredToken_KeepsTheOldHistory()
    {
        using var folder = new TempFolder();
        var source = new EaHistorySource(new CacheStore(folder.Path));
        await source.RefreshAsync(new EaClient(new HttpClient(new FakeEa()), Token));

        var expired = new HttpClient(new StatusHandler(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<EaException>(() => source.RefreshAsync(new EaClient(expired, Token)));

        Assert.Equal(2, source.LoadCached()!.Games.Count);
    }

    [Fact]
    public async Task RefreshAsync_OneGameWhoseAchievementsFail_IsAskedAgainNextTime()
    {
        using var folder = new TempFolder();
        var ea = new FakeEa { AnthemAchievementsError = HttpStatusCode.InternalServerError };
        var source = new EaHistorySource(new CacheStore(folder.Path));

        var history = await source.RefreshAsync(new EaClient(new HttpClient(ea), Token));

        // The other games and the history are kept; Anthem has no achievements yet.
        Assert.Equal(2, source.LoadCached()!.Games.Count);
        Assert.Equal(PlayStatus.Mastered, history.Games[0].Status);
        Assert.Null(history.Games[1].TotalAchievements);

        ea.AnthemAchievementsError = null;
        ea.Requests.Clear();
        await source.RefreshAsync(new EaClient(new HttpClient(ea), Token));
        Assert.Single(ea.Requests, r => r.Kind == "achievements");
    }

    [Fact]
    public async Task LoadCached_ACorruptedCacheCountsAsMissing()
    {
        using var folder = new TempFolder();
        var cache = new CacheStore(folder.Path);
        await cache.SaveAsync("ea-history", "{ not json");

        Assert.Null(new EaHistorySource(cache).LoadCached());
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("") });
    }
}
