using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GameRadar.Core.Cache;
using GameRadar.Core.Epic;
using GameRadar.Core.Models;
using GameRadar.Core.Settings;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Epic;

public class EpicHistorySourceTests : IDisposable
{
    private const string Code = "0123456789abcdef0123456789abcdef";
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    /// <summary>
    /// A fake Epic: a library of 5 entries over 2 pages; 4 launched (a game with
    /// achievements, a game without, its DLC, Unreal Engine) plus one launched
    /// game the catalog does not know. Counts the requests by kind.
    /// </summary>
    private sealed class FakeEpic : HttpMessageHandler
    {
        public int RivalsMinutes { get; set; } = 243;
        public HttpStatusCode? RivalsTitleError { get; set; }
        /// <summary>The catalog unreachable: every title request fails as the network would.</summary>
        public bool CatalogUnreachable { get; set; }
        /// <summary>A library whose cursor never ends.</summary>
        public bool EndlessLibrary { get; set; }
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            string path = uri.AbsolutePath;
            if (path.EndsWith("/oauth/token"))
                return Json("""{"access_token":"eg1~a","expires_in":129600,"refresh_token":"eg1~r","account_id":"acc"}""");
            if (path.EndsWith("/library/api/public/items"))
            {
                Requests.Add("library");
                if (EndlessLibrary)
                    return Json("""{"records":[],"responseMetadata":{"nextCursor":"again"}}""");
                return Json(uri.Query.Contains("cursor=")
                    ? """{"records":[{"appName":"Engine","namespace":"ue","catalogItemId":"i-ue"},{"appName":"Owned","namespace":"ns-o","catalogItemId":"i-o"}],"responseMetadata":{}}"""
                    : """{"records":[{"appName":"Live","namespace":"ns-rivals","catalogItemId":"i-rivals"},{"appName":"Catnip","namespace":"ns-bl3","catalogItemId":"i-bl3"},{"appName":"Aztec","namespace":"ns-civ","catalogItemId":"i-dlc"},{"appName":"Gone","namespace":"ns-gone","catalogItemId":"i-gone"}],"responseMetadata":{"nextCursor":"p2"}}""");
            }
            if (path.Contains("/playtime/"))
            {
                Requests.Add("playtime");
                return Json($$"""[{"artifactId":"Live","totalTime":{{RivalsMinutes * 60}}},{"artifactId":"Catnip","totalTime":228780},{"artifactId":"Aztec","totalTime":20940},{"artifactId":"Engine","totalTime":16860},{"artifactId":"Gone","totalTime":600},{"artifactId":"NotInLibrary","totalTime":600}]""");
            }
            if (path.Contains("/bulk/items"))
            {
                Requests.Add("title");
                if (CatalogUnreachable)
                    throw new HttpRequestException("unreachable");
                string id = System.Web.HttpUtility.ParseQueryString(uri.Query)["id"]!;
                if (id == "i-rivals" && RivalsTitleError is { } error)
                    return new HttpResponseMessage(error) { Content = new StringContent("") };
                return Json(id switch
                {
                    "i-rivals" => """{"i-rivals":{"title":"Marvel Rivals","categories":[{"path":"games"}]}}""",
                    "i-bl3" => """{"i-bl3":{"title":"Borderlands 3","categories":[{"path":"games"}]}}""",
                    "i-dlc" => """{"i-dlc":{"title":"Civilization VI: Aztec DLC","categories":[{"path":"addons"}]}}""",
                    "i-ue" => """{"i-ue":{"title":"Unreal Engine","categories":[{"path":"engines/ue5"},{"path":"engines"}]}}""",
                    _ => "{}"
                });
            }
            // GraphQL achievements: Marvel Rivals 5/40, the others none.
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            bool rivals = body["variables"]!["sandboxId"]!.GetValue<string>() == "ns-rivals";
            if (body["query"]!.GetValue<string>().Contains("PlayerAchievement"))
            {
                Requests.Add("unlocked");
                return Json(rivals
                    ? """{"data":{"PlayerAchievement":{"playerAchievementGameRecordsBySandbox":{"records":[{"totalUnlocked":5}]}}}}"""
                    : """{"data":{"PlayerAchievement":{"playerAchievementGameRecordsBySandbox":{"records":null}}}}""");
            }
            Requests.Add("total");
            return Json("""{"data":{"Achievement":{"productAchievementsRecordBySandbox":{"totalAchievements":""" + (rivals ? "40" : "null") + "}}}}");
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private async Task<EpicClient> ClientAsync(FakeEpic epic)
    {
        var account = new EpicAccount(new HttpClient(epic), new SecretStore(_folder.Path));
        await account.ConnectAsync(Code);
        return new EpicClient(new HttpClient(epic), account);
    }

    private EpicHistorySource Source() => new(new CacheStore(Path.Combine(_folder.Path, "cache")));

    [Fact]
    public async Task RefreshAsync_KeepsThePlayedGamesWithTheirTitlesAndAchievements()
    {
        var epic = new FakeEpic();

        var history = await Source().RefreshAsync(await ClientAsync(epic));

        // Most played first; the DLC, the engine and the games without a known title left out.
        Assert.Equal(["Borderlands 3", "Marvel Rivals"], history.Games.Select(g => g.Name));
        var borderlands = history.Games[0];
        Assert.Equal(("Epic", "Catnip", 3813), (borderlands.Platform, borderlands.PlatformId, borderlands.PlaytimeMinutes));
        Assert.False(borderlands.HasAchievements);
        Assert.Null(borderlands.LastPlayed);
        var rivals = history.Games[1];
        Assert.Equal((5, 40), (rivals.UnlockedAchievements, rivals.TotalAchievements));
        Assert.Equal(PlayStatus.Played, rivals.Status);
        // Both library pages read.
        Assert.Equal(2, epic.Requests.Count(r => r == "library"));
    }

    [Fact]
    public async Task LoadCached_ReadsTheSavedHistoryBack()
    {
        var source = Source();
        Assert.Null(source.LoadCached());
        await source.RefreshAsync(await ClientAsync(new FakeEpic()));

        var cached = Source().LoadCached()!;

        Assert.Equal(["Borderlands 3", "Marvel Rivals"], cached.Games.Select(g => g.Name));
        Assert.Equal(40, cached.Games[1].TotalAchievements);
    }

    [Fact]
    public async Task ASecondRefresh_AsksTitlesNeverAgainAndAchievementsOnlyForAGamePlayedSince()
    {
        var epic = new FakeEpic();
        var source = Source();
        await source.RefreshAsync(await ClientAsync(epic));
        epic.Requests.Clear();

        await source.RefreshAsync(await ClientAsync(epic));
        // Known titles: none asked again but the one the catalog did not know.
        Assert.Equal(1, epic.Requests.Count(r => r == "title"));
        Assert.DoesNotContain(epic.Requests, r => r is "total" or "unlocked");

        // Marvel Rivals played since: its achievements only.
        epic.RivalsMinutes = 300;
        epic.Requests.Clear();
        await source.RefreshAsync(await ClientAsync(epic));
        Assert.Equal(["total", "unlocked"], epic.Requests.Where(r => r is "total" or "unlocked"));
    }

    [Fact]
    public async Task AGameWhoseTitleFails_IsSkippedAndAskedAgainNextTime()
    {
        var epic = new FakeEpic { RivalsTitleError = HttpStatusCode.InternalServerError };
        var source = Source();

        var history = await source.RefreshAsync(await ClientAsync(epic));

        Assert.Equal(["Borderlands 3"], history.Games.Select(g => g.Name));
        epic.RivalsTitleError = null;
        Assert.Equal(["Borderlands 3", "Marvel Rivals"], (await source.RefreshAsync(await ClientAsync(epic))).Games.Select(g => g.Name));
    }

    [Fact]
    public async Task ARateLimit_StopsButKeepsTheTitlesAlreadyRead()
    {
        var epic = new FakeEpic { RivalsTitleError = HttpStatusCode.TooManyRequests };
        var source = Source();

        var ex = await Assert.ThrowsAsync<EpicException>(async () => await source.RefreshAsync(await ClientAsync(epic)));

        Assert.True(ex.IsRateLimited);
        // Nothing saved as history: the previous one (none here) stays.
        Assert.Null(source.LoadCached());

        // The titles read before the limit (Borderlands 3, the DLC, the engine) are not asked again.
        epic.RivalsTitleError = null;
        epic.Requests.Clear();
        await source.RefreshAsync(await ClientAsync(epic));
        Assert.Equal(2, epic.Requests.Count(r => r == "title"));
    }

    [Fact]
    public async Task AnUnreachableCatalog_StopsAfterTheFirstTryAndKeepsTheHistory()
    {
        var epic = new FakeEpic { CatalogUnreachable = true };
        var source = Source();

        var history = await source.RefreshAsync(await ClientAsync(epic));

        // No title known yet: no game shown, but the library is saved and nothing waits game after game.
        Assert.Empty(history.Games);
        Assert.Equal(1, epic.Requests.Count(r => r == "title"));
        Assert.NotNull(source.LoadCached());
    }

    [Fact]
    public async Task ALibraryThatNeverEnds_IsNotSavedCutShort()
    {
        var epic = new FakeEpic { EndlessLibrary = true };
        var source = Source();

        var ex = await Assert.ThrowsAsync<EpicException>(async () => await source.RefreshAsync(await ClientAsync(epic)));

        Assert.Equal(GameRadar.Core.Resources.CoreStrings.Epic_Format, ex.Message);
        Assert.Null(source.LoadCached());
    }

    [Fact]
    public async Task LoadCached_ACorruptedCacheCountsAsMissing()
    {
        var cache = new CacheStore(Path.Combine(_folder.Path, "cache"));
        await cache.SaveAsync("epic-history", "{ not json");

        Assert.Null(new EpicHistorySource(cache).LoadCached());
    }
}
