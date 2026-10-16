using System.Collections.Concurrent;
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
        public ConcurrentQueue<(string Kind, int Slugs)> Requests { get; } = new();
        /// <summary>Every EA game, in its order (the played ones: anthem, star-wars-jedi-fallen-order).</summary>
        public IReadOnlyList<string> Catalog { get; init; } = [.. Enumerable.Range(1, 10).Select(i => $"game-{i}"), "anthem", "star-wars-jedi-fallen-order"];
        /// <summary>Set: the recentGames request with this slug answers 100 ms late.</summary>
        public string? SlowRecent { get; init; }
        /// <summary>Set: the recentGames request with this slug answers this error.</summary>
        public (string Slug, HttpStatusCode Status)? RecentError { get; init; }
        /// <summary>Every slug asked is a played game (a full page).</summary>
        public bool EverySlugPlayed { get; init; }
        /// <summary>Set: each request of this kind ("recent", "achievements") waits (5 s at most) until another one runs too: proves they overlap, without timing.</summary>
        public string? Overlapping { get; init; }
        public OverlapProbe Probe { get; } = new();
        /// <summary>Every achievements request fails as the network would.</summary>
        public bool AchievementsUnreachable { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            string query = body["query"]!.GetValue<string>();
            var slugs = (body["variables"]?["gameSlugs"] ?? body["variables"]?["slugs"])?.AsArray().Select(s => s!.GetValue<string>()).ToList() ?? [];
            string kind = query.Contains("gameSearch") ? "search" : query.Contains("recentGames") ? "recent"
                : query.Contains("games(") ? "games" : query.Contains("achievements") ? "achievements" : "identity";
            Requests.Enqueue((kind, slugs.Count));
            if (kind == Overlapping)
                await Probe.WaitForAnotherAsync(ct);
            if (kind == "recent")
            {
                if (SlowRecent is { } slow && slugs.Contains(slow))
                    await Task.Delay(100, ct);
                if (RecentError is { } recentError && slugs.Contains(recentError.Slug))
                    return new HttpResponseMessage(recentError.Status) { Content = new StringContent("") };
            }
            if (kind == "achievements" && AchievementsUnreachable)
                throw new HttpRequestException("unreachable");
            if (kind == "achievements" && AnthemAchievementsError is { } error
                && body["variables"]!["offerId"]!.GetValue<string>() == "offer-anthem")
                return new HttpResponseMessage(error) { Content = new StringContent("") };
            string json = kind switch
            {
                "search" => """{"data":{"gameSearch":{"items":[""" + string.Join(",", Catalog.Select(s => $$"""{"slug":"{{s}}"}""")) + "]}}}",
                "recent" => """{"data":{"me":{"recentGames":{"items":[""" + string.Join(",", slugs.Where(s => EverySlugPlayed || s is "anthem" or "star-wars-jedi-fallen-order").Select(s => s == "anthem"
                    ? """{"gameSlug":"anthem","lastSessionEndDate":"2021-02-01T20:00:00.000Z","totalPlayTimeSeconds":35640}"""
                    : $$"""{"gameSlug":"star-wars-jedi-fallen-order","lastSessionEndDate":"{{JediLastSession}}","totalPlayTimeSeconds":231120}""")) + "]}}}}",
                "games" => """{"data":{"games":{"items":[""" + string.Join(",", slugs.Select(s => $$$"""{"slug":"{{{s}}}","products":{"items":[{"name":"{{{(s == "anthem" ? "Anthem™" : "STAR WARS Jedi: Fallen Order™")}}}","originOfferId":"offer-{{{s}}}","baseItem":{"gameType":"BASE_GAME"}}]}}""")) + "]}}}",
                "achievements" => body["variables"]!["offerId"]!.GetValue<string>() == "offer-anthem"
                    ? """{"data":{"achievements":[{"achievements":[{"awardCount":1},{"awardCount":0},{"awardCount":0},{"awardCount":0}]}]}}"""
                    : """{"data":{"achievements":[{"achievements":[{"awardCount":1},{"awardCount":1}]}]}}""",
                _ => """{"data":{"me":{"player":{"psd":"1000123"}}}}"""
            };
            return Json(json);
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
    public async Task RefreshAsync_AsksAHundredSlugsPerRequest()
    {
        using var folder = new TempFolder();
        var ea = new FakeEa { Catalog = [.. Enumerable.Range(1, 248).Select(i => $"game-{i}"), "anthem", "star-wars-jedi-fallen-order"] };

        await new EaHistorySource(new CacheStore(folder.Path)).RefreshAsync(new EaClient(new HttpClient(ea), Token));

        // 250 slugs: 3 recentGames requests of 100 at most (sent several at a time: any order);
        // the names of the played games, 5 at most per request.
        Assert.Equal([50, 100, 100], ea.Requests.Where(r => r.Kind == "recent").Select(r => r.Slugs).Order());
        Assert.All(ea.Requests.Where(r => r.Kind == "games"), r => Assert.InRange(r.Slugs, 1, EaClient.GamesBatchSize));
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
    public async Task RefreshAsync_AsksTheRecentGamesFourAtATimeAtMost()
    {
        using var folder = new TempFolder();
        // 800 games: 8 requests, each waiting for another one to run too.
        var ea = new FakeEa
        {
            Catalog = [.. Enumerable.Range(1, 798).Select(i => $"game-{i}"), "anthem", "star-wars-jedi-fallen-order"],
            Overlapping = "recent"
        };
        var source = new EaHistorySource(new CacheStore(folder.Path));

        var history = await source.RefreshAsync(new EaClient(new HttpClient(ea), Token));

        Assert.Equal(2, history.Games.Count);
        Assert.InRange(ea.Probe.MaxAtOnce, 2, EaClient.RequestsAtOnce);
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheAnswersInTheOrderOfTheBatches()
    {
        using var folder = new TempFolder();
        var cache = new CacheStore(folder.Path);
        // Anthem in the first batch, Jedi in the last: the first one answers last.
        var ea = new FakeEa
        {
            Catalog = ["anthem", .. Enumerable.Range(1, 149).Select(i => $"game-{i}"), "star-wars-jedi-fallen-order"],
            SlowRecent = "anthem"
        };

        await new EaHistorySource(cache).RefreshAsync(new EaClient(new HttpClient(ea), Token));

        // Saved as a series of requests would have saved it.
        string recentGames = cache.TryLoad("ea-history")!.Content.Split("\"games\":")[0];
        Assert.True(recentGames.IndexOf("\"anthem\"", StringComparison.Ordinal) < recentGames.IndexOf("star-wars-jedi-fallen-order", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RefreshAsync_OneBatchInError_KeepsTheOldHistory(HttpStatusCode status)
    {
        using var folder = new TempFolder();
        var source = new EaHistorySource(new CacheStore(folder.Path));
        await source.RefreshAsync(new EaClient(new HttpClient(new FakeEa()), Token));

        // Only the batch with Jedi fails: an incomplete history is never saved.
        var failing = new FakeEa { RecentError = ("star-wars-jedi-fallen-order", status), JediLastSession = "2026-10-01T20:00:00.000Z" };
        await Assert.ThrowsAsync<EaException>(() => source.RefreshAsync(new EaClient(new HttpClient(failing), Token)));

        var kept = source.LoadCached()!;
        Assert.Equal(2, kept.Games.Count);
        Assert.Equal(new DateTimeOffset(2023, 5, 7, 15, 26, 58, TimeSpan.Zero), kept.Games.Single(g => g.PlatformId == "star-wars-jedi-fallen-order").LastPlayed);
    }

    [Fact]
    public async Task RefreshAsync_AFullPage_IsRefusedRatherThanCut()
    {
        using var folder = new TempFolder();
        var source = new EaHistorySource(new CacheStore(folder.Path));
        await source.RefreshAsync(new EaClient(new HttpClient(new FakeEa()), Token));

        // As many played games as slugs asked: the page may have been cut.
        var full = new FakeEa { Catalog = [.. Enumerable.Range(1, EaClient.RecentGamesBatchSize).Select(i => $"game-{i}")], EverySlugPlayed = true };
        await Assert.ThrowsAsync<EaException>(() => source.RefreshAsync(new EaClient(new HttpClient(full), Token)));

        Assert.Equal(2, source.LoadCached()!.Games.Count);
    }

    [Fact]
    public async Task RefreshAsync_AsksTheAchievementsSeveralAtATime()
    {
        using var folder = new TempFolder();
        // Both played games' achievements, each waiting for the other to run too.
        var ea = new FakeEa { Overlapping = "achievements" };

        var history = await new EaHistorySource(new CacheStore(folder.Path)).RefreshAsync(new EaClient(new HttpClient(ea), Token));

        Assert.InRange(ea.Probe.MaxAtOnce, 2, EaClient.RequestsAtOnce);
        Assert.All(history.Games, g => Assert.NotNull(g.TotalAchievements));
    }

    [Fact]
    public async Task RefreshAsync_ARateLimitOnTheAchievements_StillStopsTheSync()
    {
        using var folder = new TempFolder();
        var ea = new FakeEa { AnthemAchievementsError = HttpStatusCode.TooManyRequests };

        // The other requests stopped with it are skipped: the rate limit is what goes up.
        var error = await Assert.ThrowsAsync<EaException>(() => new EaHistorySource(new CacheStore(folder.Path)).RefreshAsync(new EaClient(new HttpClient(ea), Token)));

        Assert.True(error.IsRateLimited);
    }

    [Fact]
    public async Task RefreshAsync_UnreachableAchievements_StopAfterTheRequestsAlreadySentAndKeepTheHistory()
    {
        using var folder = new TempFolder();
        // 20 games played: without the stop, 20 requests waiting 30 s each, 4 at a time.
        var ea = new FakeEa { Catalog = [.. Enumerable.Range(1, 20).Select(i => $"game-{i}")], EverySlugPlayed = true, AchievementsUnreachable = true };
        var source = new EaHistorySource(new CacheStore(folder.Path));

        var history = await source.RefreshAsync(new EaClient(new HttpClient(ea), Token));

        Assert.NotEmpty(history.Games);
        Assert.InRange(ea.Requests.Count(r => r.Kind == "achievements"), 1, EaClient.RequestsAtOnce);
        Assert.NotNull(source.LoadCached());
    }

    [Fact]
    public async Task LoadCached_ReadsAHistorySavedInBatchesOfFive()
    {
        // Before 1.6.2: one answer per batch of 5 slugs, here Anthem and Jedi in two of them.
        using var folder = new TempFolder();
        var cache = new CacheStore(folder.Path);
        await cache.SaveAsync("ea-history", """
            {"recentGames":[
              {"data":{"me":{"recentGames":{"items":[{"gameSlug":"anthem","lastSessionEndDate":"2021-02-01T20:00:00.000Z","totalPlayTimeSeconds":35640}]}}}},
              {"data":{"me":{"recentGames":{"items":[{"gameSlug":"star-wars-jedi-fallen-order","lastSessionEndDate":"2023-05-07T15:26:58.000Z","totalPlayTimeSeconds":231120}]}}}}],
             "games":[
              {"data":{"games":{"items":[{"slug":"anthem","products":{"items":[{"name":"Anthem™","originOfferId":"offer-anthem","baseItem":{"gameType":"BASE_GAME"}}]}}]}}}]}
            """);

        var history = new EaHistorySource(cache).LoadCached()!;

        Assert.Equal(["star-wars-jedi-fallen-order", "anthem"], history.Games.Select(g => g.PlatformId));
        Assert.Equal("Anthem™", history.Games[1].Name);
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
