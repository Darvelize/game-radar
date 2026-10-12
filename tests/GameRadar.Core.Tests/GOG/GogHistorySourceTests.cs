using System.Net;
using System.Text;
using GameRadar.Core.Cache;
using GameRadar.Core.GOG;
using GameRadar.Core.Models;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.GOG;

public class GogHistorySourceTests
{
    private static string Item(string id, string title, string stats = "[]") => $$"""
        { "game": { "id": "{{id}}", "title": "{{title}}", "achievementSupport": true }, "stats": {{stats}} }
        """;

    private static string Page(int pages, params string[] items) =>
        $$"""{ "page": 1, "limit": 50, "pages": {{pages}}, "_embedded": { "items": [{{string.Join(",", items)}}] } }""";

    private static string Stats(int minutes, string lastSession, double? achievements = null) =>
        $$"""{ "1": { "playtime": {{minutes}}, "lastSession": "{{lastSession}}"{{(achievements is { } a ? $", \"achievementsPercentage\": {a}" : "")}} } }""";

    /// <summary>A profile of two pages: a finished game and an owned one on page 1, a recent one on page 2.</summary>
    private static string TwoPages(Uri uri) => uri.Query.Contains("page=2")
        ? Page(2, Item("3", "Recent Game", Stats(120, "2026-10-01T20:00:00+00:00", achievements: 10)))
        : Page(2, Item("1", "Finished Game", Stats(3000, "2026-05-01T20:00:00+00:00", achievements: 100)), Item("2", "Owned Game"));

    [Fact]
    public async Task RefreshAsync_ReadsEveryPageAndLoadCachedReadsThemBack()
    {
        using var folder = new TempFolder();
        var source = new GogHistorySource(new CacheStore(folder.Path));
        var asked = new List<Uri>();

        var history = await source.RefreshAsync(ClientAnswering(uri => { asked.Add(uri); return TwoPages(uri); }), "Radar_Player");

        Assert.Equal(2, asked.Count);
        // Most recently played first; the owned game Galaxy never followed last.
        Assert.Equal(["Recent Game", "Finished Game", "Owned Game"], history.Games.Select(g => g.Name));
        Assert.Equal(["Recent Game", "Finished Game", "Owned Game"], source.LoadCached()!.Games.Select(g => g.Name));
    }

    [Fact]
    public async Task RefreshAsync_TurnsTheStatsIntoPlayedGames()
    {
        using var folder = new TempFolder();
        var history = await new GogHistorySource(new CacheStore(folder.Path)).RefreshAsync(ClientAnswering(TwoPages), "Radar_Player");

        var finished = history.Games.Single(g => g.Name == "Finished Game");
        Assert.Equal("GOG", finished.Platform);
        Assert.Equal("1", finished.PlatformId);
        Assert.Equal(3000, finished.PlaytimeMinutes);
        Assert.True(finished.HasAchievements);
        Assert.Equal(100, finished.ProgressPercentage);
        // Only a percentage: no count of achievements.
        Assert.Null(finished.TotalAchievements);
        Assert.Equal(PlayStatus.Mastered, finished.Status);
        Assert.Equal(PlayStatus.Played, history.Games.Single(g => g.Name == "Recent Game").Status);

        // Owned, never followed by Galaxy: never launched, as a Steam game with 0 min
        // (the user's choice): says nothing of the taste, may still be recommended.
        var owned = history.Games.Single(g => g.Name == "Owned Game");
        Assert.Equal(0, owned.PlaytimeMinutes);
        Assert.True(owned.NeverPlayed);
        Assert.False(owned.HasAchievements);
        Assert.False(owned.HasEngagementSignal);
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheOldHistoryWhenAPageFails()
    {
        using var folder = new TempFolder();
        var source = new GogHistorySource(new CacheStore(folder.Path));
        await source.RefreshAsync(ClientAnswering(TwoPages), "Radar_Player");

        // The second page is a maintenance page this time.
        await Assert.ThrowsAsync<GogException>(() => source.RefreshAsync(
            ClientAnswering(uri => uri.Query.Contains("page=2") ? "<html>Maintenance</html>" : TwoPages(uri)), "Radar_Player"));

        Assert.Equal(3, source.LoadCached()!.Games.Count);
    }

    [Fact]
    public async Task LoadCached_ACorruptedCacheCountsAsMissing()
    {
        using var folder = new TempFolder();
        var cache = new CacheStore(folder.Path);
        await cache.SaveAsync("gog-history", "{ not json");

        Assert.Null(new GogHistorySource(cache).LoadCached());
    }

    private static GogClient ClientAnswering(Func<Uri, string> answer) => new(new HttpClient(new FakeHandler(answer)));

    private sealed class FakeHandler(Func<Uri, string> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answer(request.RequestUri!), Encoding.UTF8, "application/hal+json")
            });
    }
}
