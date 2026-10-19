using System.Net;
using System.Text;
using GameRadar.Core.Cache;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Steam;

public class SteamTagTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    // ---------- Titles (the cleaning itself: GameTitleTests) ----------

    [Fact]
    public void FindExactTitle_DoesNotTakeASequel()
    {
        var sequel = ("1145350", "Hades II", Weights(1774, 1.0));
        var original = ("1145360", "Hades", Weights(4231, 1.0));

        var found = SteamTagClient.FindExactTitle([sequel, original], "Hades");

        Assert.Equal("1145360", found!.Value.AppId);
    }

    [Fact]
    public void ToTagWeights_IsRelativeToTheMostVotedAndDropsBusinessTags()
    {
        // 113 = Free to Play, 12095 = Sexual Content: say nothing about what the game is like.
        var weights = SteamTagClient.ToTagWeights([(113, 2000), (12095, 1500), (1774, 1000), (19, 500)]);

        Assert.Equal(new Dictionary<int, double> { [1774] = 1.0, [19] = 0.5 }, weights);
    }

    // ---------- Source: what is looked up, and cached ----------

    private static IReadOnlyDictionary<int, double> Weights(int tag, double weight) => new Dictionary<int, double> { [tag] = weight };

    private static string Items(params (int AppId, string Name)[] items) =>
        $$"""{ "response": { "store_items": [{{string.Join(",", items.Select(i =>
            $$"""{ "success": 1, "appid": {{i.AppId}}, "name": "{{i.Name}}", "tags": [{ "tagid": 1774, "weight": 900 }] }"""))}}] } }""";

    [Fact]
    public async Task FetchMissing_LooksUpEveryGameThatCountsAndGamePassCandidatesOnceThenUsesTheCache()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new FakeHandler(uri =>
        {
            string input = Uri.UnescapeDataString(uri.Query);
            lock (requests)
                requests.Add(uri.AbsolutePath.Contains("GetItems") ? "items" : "search:" + input);
            if (uri.AbsolutePath.Contains("GetItems"))
                return Items((1085660, "Destiny 2"));
            return input.Contains("Control") ? Items((870780, "Control")) : Items();
        }));
        var client = new SteamTagClient(http);
        var played = new[]
        {
            Played("x1", 100, PlayStatus.Mastered, name: "Control"),                                    // favorite, Xbox: by title
            Played("1085660", 60, name: "Destiny 2", platform: "Steam"),                                // favorite, Steam: by app id
            Played("x2", 2, name: "Dropped Game"),                                                      // barely played: skipped
            Played("x3", 30, name: "Half Played")                                                       // counts, not a favorite: by title too
        };
        var candidates = new[]
        {
            Catalog("9A", title: "Unknown On Steam - Standard Edition"),                                // Game Pass: by title
            Catalog("steam:1", title: "Steam Hit", steamAppId: "1", inGamePass: false) with { Tags = Weights(19, 1) } // known already
        };

        var source = new SteamTagSource(new CacheStore(_folder.Path));
        await source.FetchMissingAsync(played, candidates, client);

        Assert.Equal(4, requests.Count);
        Assert.Contains("items", requests);
        // The profile is learned on tags too: every game that counts, the favorites first. Two
        // searches leave at once (after the app ids): the played games, then the Game Pass one.
        Assert.InRange(requests.FindIndex(r => r.Contains("Control")), 1, SteamTagSource.TitleSearchesAtOnce);
        Assert.Equal(3, requests.FindIndex(r => r.Contains("Unknown On Steam")));
        Assert.DoesNotContain(requests, r => r.Contains("Dropped Game") || r.Contains("Steam Hit"));

        var index = source.LoadIndex(candidates);
        Assert.NotNull(index.For(played[0]));
        Assert.NotNull(index.For(played[1]));
        Assert.Null(index.For(candidates[0]));   // not found on Steam
        Assert.NotNull(index.For(candidates[1]));

        // A new session, same games: everything comes from the cache, even "not found".
        requests.Clear();
        var reopened = new SteamTagSource(new CacheStore(_folder.Path));
        Assert.Equal(0, reopened.CountMissing(reopened.LoadIndex(candidates), played, candidates, DateTimeOffset.UtcNow));
        await reopened.FetchMissingAsync(played, candidates, client);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task FetchMissing_KeepsWhatWasFoundWhenSteamRefuses()
    {
        var played = new[]
        {
            Played("x1", 100, PlayStatus.Mastered, name: "Control"),
            Played("x2", 100, PlayStatus.Mastered, name: "Alan Wake 2")
        };
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        // Alan Wake is refused once Control's tags are kept: one found, then a refusal.
        using var http = new HttpClient(new FakeHandler(uri => IsControl(uri) ? Items((870780, "Control"))
            : RefusedOnce(() => source.LoadIndex([]).For(played[0]) is not null)));

        // The second search is refused (rate limit): no error, the first result stays.
        await source.FetchMissingAsync(played, [], new SteamTagClient(http));

        var index = source.LoadIndex([]);
        Assert.NotNull(index.For(played[0]));
        Assert.Equal(1, source.CountMissing(index, played, [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task FetchMissing_KeepsWhatWasFoundWhenTheNetworkFails()
    {
        var played = new[]
        {
            Played("x1", 100, PlayStatus.Mastered, name: "Control"),
            Played("x2", 100, PlayStatus.Mastered, name: "Alan Wake 2")
        };
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        using var http = new HttpClient(new FakeHandler(uri => IsControl(uri) ? Items((870780, "Control"))
            : RefusedOnce(() => source.LoadIndex([]).For(played[0]) is not null) ?? throw new HttpRequestException("The Steam store cannot be reached.")));

        // Like a refusal: no error, the first result stays, the second waits.
        await source.FetchMissingAsync(played, [], new SteamTagClient(http));

        var reopened = new SteamTagSource(new CacheStore(_folder.Path));
        var index = reopened.LoadIndex([]);
        Assert.NotNull(index.For(played[0]));
        Assert.Equal(1, reopened.CountMissing(index, played, [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task FetchMissing_LooksUpMyGames_ByAppIdOrTitle()
    {
        // "Mes jeux": never launched on Steam (app id), barely played on Xbox (title).
        var requests = new List<string>();
        using var http = new HttpClient(new FakeHandler(uri =>
        {
            string input = Uri.UnescapeDataString(uri.Query);
            requests.Add(uri.AbsolutePath.Contains("GetItems") ? "items" : "search:" + input);
            return Items((620, "Portal 2"));
        }));
        var owned = new OwnedGame(["Steam"], 0, null, NeverPlayed: true);
        var candidates = new[]
        {
            Catalog("mine:portal2", title: "Portal 2", steamAppId: "620", inGamePass: false) with { Owned = owned },
            Catalog("mine:roguewaters", title: "Rogue Waters", inGamePass: false) with { Owned = owned with { Platforms = ["Xbox"] } }
        };
        var source = new SteamTagSource(new CacheStore(_folder.Path));

        await source.FetchMissingAsync([], candidates, new SteamTagClient(http));

        Assert.Contains("items", requests);
        Assert.Contains(requests, r => r.Contains("Rogue Waters"));
        Assert.NotNull(source.LoadIndex(candidates).For(candidates[0]));
    }

    [Fact]
    public async Task FetchMissing_LooksUpAGameAddedByHandByItsSteamAppId()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new FakeHandler(uri =>
        {
            requests.Add(uri.AbsolutePath.Contains("GetItems") ? "items" : "search");
            return Items((1145360, "Hades"));
        }));
        // Played on Switch, picked from the Steam search: its title may differ, its app id does not.
        var played = new[] { ManualGames.ToPlayed(new ManualGame("Hades (Switch)", ManualPlatform.Switch, ManualLevel.Completed, SteamAppId: "1145360")) };
        var source = new SteamTagSource(new CacheStore(_folder.Path));

        await source.FetchMissingAsync(played, [], new SteamTagClient(http));

        Assert.Equal(["items"], requests);
        Assert.NotNull(source.LoadIndex([]).For(played[0]));
    }

    [Fact]
    public async Task FetchMissing_LooksUpTheFavoritesAddedByHandByTitle()
    {
        // Played on Switch, so no Steam or Xbox id: found by its title.
        using var http = new HttpClient(new FakeHandler(_ => Items((1145360, "Hades"))));
        var played = new[] { ManualGames.ToPlayed(new ManualGame("Hades", ManualPlatform.Switch, ManualLevel.Completed)) };
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        Assert.Equal(1, source.CountMissing(source.LoadIndex([]), played, [], DateTimeOffset.UtcNow));

        await source.FetchMissingAsync(played, [], new SteamTagClient(http));

        Assert.NotNull(source.LoadIndex([]).For(played[0]));
    }

    [Fact]
    public void CountMissing_LooksUpTheGamesOnlyGogSellsByTitle()
    {
        // A GOG game also on Steam takes the Steam entry's tags; one only on GOG has none.
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var onlyOnGog = Catalog("gog:1", title: "Disco Elysium", inGamePass: false) with { GogStoreLink = "https://www.gog.com/en/game/1" };

        Assert.Equal(1, source.CountMissing(source.LoadIndex([onlyOnGog]), [], [onlyOnGog], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void TopNames_GivesTheThreeMostVotedTagsByName()
    {
        var tags = new Dictionary<int, double> { [1] = 0.5, [2] = 1.0, [3] = 0.9, [4] = 0.7, [5] = 0.2 };
        var names = new Dictionary<int, string> { [1] = "RPG", [2] = "Tir avec butin", [3] = "Multijoueur", [4] = "Coop en ligne", [5] = "Action" };

        Assert.Equal(["Tir avec butin", "Multijoueur", "Coop en ligne"], SteamTagIndex.TopNames(tags, names));
    }

    [Fact]
    public void TopNames_SkipsATagWithoutNameAndKeepsSteamsOrderOnATie()
    {
        // Equal votes: the order Steam gave them in stays.
        var tags = new Dictionary<int, double> { [7] = 1.0, [8] = 0.8, [9] = 0.8, [10] = 0.8 };
        var names = new Dictionary<int, string> { [8] = "Monde ouvert", [9] = "RPG", [10] = "Espace" };

        Assert.Equal(["Monde ouvert", "RPG", "Espace"], SteamTagIndex.TopNames(tags, names));
    }

    [Fact]
    public void TopTags_GivesTheIdsWithTheirNames_AsManyAsAsked()
    {
        var tags = new Dictionary<int, double> { [1] = 0.5, [2] = 1.0, [3] = 0.9, [4] = 0.7 };
        var names = new Dictionary<int, string> { [1] = "RPG", [2] = "Tir avec butin", [3] = "Multijoueur", [4] = "Coop en ligne" };

        Assert.Equal([(2, "Tir avec butin"), (3, "Multijoueur"), (4, "Coop en ligne"), (1, "RPG")], SteamTagIndex.TopTags(tags, names, count: 4));
    }

    [Fact]
    public void TopNames_NoTagsOrNoNames_IsEmpty()
    {
        Assert.Empty(SteamTagIndex.TopNames(null, new Dictionary<int, string> { [1] = "RPG" }));
        Assert.Empty(SteamTagIndex.TopNames(new Dictionary<int, double> { [1] = 1 }, new Dictionary<int, string>()));
    }

    [Fact]
    public async Task FetchMissing_LooksUpByTitleASteamGameNoLongerSold()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new FakeHandler(uri =>
        {
            bool byAppId = uri.AbsolutePath.Contains("GetItems");
            requests.Add(byAppId ? "items" : "search");
            // By app id: not visible any more, no tags. By title: the edition still sold.
            return byAppId ? Items() : Items((475150, "Titan Quest Anniversary Edition"));
        }));
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var played = new[] { Played("4540", progress: 60, name: "Titan Quest", platform: "Steam") with { SteamAppId = "4540" } };

        await source.FetchMissingAsync(played, [], new SteamTagClient(http));

        // Both in the same pass, then nothing left to look up.
        Assert.Equal(["items", "search"], requests);
        Assert.NotNull(source.LoadIndex([]).For(played[0]));
        Assert.Equal(0, source.CountMissing(source.LoadIndex([]), played, [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task FetchMissing_SearchesByTitleAtOnceASteamGameAlreadyFoundWithoutTags()
    {
        // Looked up before this rule: its app id gave nothing, not retried for 30 days.
        var cache = new CacheStore(_folder.Path);
        await cache.SaveAsync("steam-tags-v1", """{"app:4540":{"Tags":{},"CheckedAt":""" + $"\"{DateTimeOffset.UtcNow:O}\"" + "}}");
        var requests = new List<string>();
        using var http = new HttpClient(new FakeHandler(uri =>
        {
            requests.Add(uri.AbsolutePath.Contains("GetItems") ? "items" : "search");
            return Items((475150, "Titan Quest Anniversary Edition"));
        }));
        var source = new SteamTagSource(cache);
        var played = new[] { Played("4540", progress: 60, name: "Titan Quest", platform: "Steam") with { SteamAppId = "4540" } };

        await source.FetchMissingAsync(played, [], new SteamTagClient(http));

        Assert.Equal(["search"], requests);
        Assert.NotNull(source.LoadIndex([]).For(played[0]));
    }

    // ---------- Tags added by the player ----------

    private static readonly Dictionary<int, string> AddedNames = new() { [122] = "RPG", [19] = "Action", [1774] = "Tir", [3871] = "2D" };

    [Fact]
    public void ForGame_PutsTheAddedTagsFirstAtTheTopWeightWhateverThePlatform()
    {
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var onSteam = Catalog("steam:1", title: "Hades", inGamePass: false) with { Tags = new Dictionary<int, double> { [1774] = 1.0, [19] = 0.5 } };
        // Keyed by title (GameTitle.Key): the game whatever its platform and case.
        var added = new Dictionary<string, List<int>> { ["hades"] = [122, 19] };

        var index = source.LoadIndex([onSteam], added);

        foreach (var played in new[] { Played("1", name: "Hades"), Played("2", name: "HADES", platform: "Manual") })
        {
            var tags = index.ForGame([played])!;
            Assert.Equal(1.0, tags[122]);
            Assert.Equal(1.0, tags[19]);
            Assert.Equal(1.0, tags[1774]);
            Assert.Equal(["RPG", "Action", "Tir"], SteamTagIndex.TopNames(tags, AddedNames));
        }
    }

    [Fact]
    public void ForGame_UnknownToSteam_HasOnlyItsAddedTags()
    {
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var index = source.LoadIndex([], new Dictionary<string, List<int>> { [GameTitle.Key("Gravity Rush")] = [3871] });

        Assert.Equal(new Dictionary<int, double> { [3871] = 1.0 }, index.ForGame([Played("1", name: "Gravity Rush")]));
        Assert.Null(index.ForGame([Played("2", name: "Tearaway")]));
    }

    [Fact]
    public void ForGame_KeepsTheSteamTagsAnotherPlatformKnows()
    {
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var onSteam = Catalog("steam:1", title: "Hades on Steam", inGamePass: false) with
            { SteamAppId = "1145360", Tags = new Dictionary<int, double> { [1774] = 1.0 } };
        var index = source.LoadIndex([onSteam], new Dictionary<string, List<int>> { ["hades"] = [122] });
        // Steam does not know the Xbox title; it knows the Steam line by app id.
        var onXbox = Played("1", name: "Hades");
        var steamLine = Played("2", name: "Hades", platform: "Steam") with { SteamAppId = "1145360" };

        Assert.Equal([122, 1774], index.ForGame([onXbox, steamLine])!.Keys);
    }

    [Fact]
    public void CountMissing_StillLooksUpOnSteamAGameThePlayerTagged()
    {
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var played = new[] { Played("1", name: "Gravity Rush", progress: 60) };
        var index = source.LoadIndex([], new Dictionary<string, List<int>> { [GameTitle.Key("Gravity Rush")] = [3871] });

        Assert.Equal(1, source.CountMissing(index, played, [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void LoadIndex_KeepsACopyOfTheAddedTags_WithAddedGivesNewOnes()
    {
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var added = new Dictionary<string, List<int>> { [GameTitle.Key("Gravity Rush")] = [3871] };
        var index = source.LoadIndex([], added);
        var game = Played("1", name: "Gravity Rush");

        // A click changes the settings while a computation reads the index.
        added[GameTitle.Key("Gravity Rush")].Add(19);
        var updated = index.WithAdded(added);

        Assert.Equal([3871], index.ForGame([game])!.Keys);
        Assert.Equal([3871, 19], updated.ForGame([game])!.Keys);
    }

    [Fact]
    public void SteamTagChoices_OffersTheStyleTagsByNameAndFindsATypedOne()
    {
        // 113 = Free to Play: not a style, ignored by the computation.
        var names = new Dictionary<int, string> { [122] = "RPG", [113] = "Free-to-play", [19] = "Action", [3871] = "2D" };

        var choices = new SteamTagChoices(names);

        Assert.Equal(["2D", "Action", "RPG"], choices.Offered.Select(t => t.Name));
        // A typed name: case, accents and symbols ignored; a tag not offered is not found.
        Assert.Equal(122, choices.IdOf("rpg"));
        Assert.Null(choices.IdOf("Free to play"));
        Assert.Null(choices.IdOf(" "));
    }

    [Fact]
    public void CountMissing_LooksUpTheGamesOnlyEpicSellsByTitle()
    {
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var onlyOnEpic = Catalog("epic:ns-aw2", title: "Alan Wake 2", inGamePass: false) with { EpicStoreLink = "https://store.epicgames.com/p/alan-wake-2" };

        Assert.Equal(1, source.CountMissing(source.LoadIndex([onlyOnEpic]), [], [onlyOnEpic], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CountMissing_LooksUpTheGamesOnlyThePlayStationStoreSellsByTitle()
    {
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var onlyOnPs = Catalog("psstore:EP-GOW", title: "God of War Ragnarök", inGamePass: false) with { PlayStationProductId = "EP-GOW" };

        Assert.Equal(1, source.CountMissing(source.LoadIndex([onlyOnPs]), [], [onlyOnPs], DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task FetchMissing_SearchesTheTitlesTwoAtATimeAtMost()
    {
        // 10 Game Pass games, without Steam app id: one title search each.
        var candidates = Enumerable.Range(1, 10).Select(i => Catalog($"gp:{i}", title: $"Game {i}")).ToList();
        var steam = new OverlappingSearches();
        using var http = new HttpClient(steam);
        var source = new SteamTagSource(new CacheStore(_folder.Path));
        var progress = new List<int>();

        await source.FetchMissingAsync([], candidates, new SteamTagClient(http), new SyncProgress(p => progress.Add(p.Done)));

        Assert.Equal(10, steam.Searches);
        Assert.InRange(steam.Probe.MaxAtOnce, 2, SteamTagSource.TitleSearchesAtOnce);
        // Never backwards, whatever order the searches end in.
        Assert.Equal(progress.Order(), progress);
    }

    /// <summary>A Steam store whose title searches each wait (5 s at most) until another one runs too.</summary>
    private sealed class OverlappingSearches : HttpMessageHandler
    {
        private int _searches;

        public OverlapProbe Probe { get; } = new();
        public int Searches => _searches;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _searches);
            await Probe.WaitForAnotherAsync(ct);
            return Json(Items((1, "Another game")));
        }
    }

    /// <summary>An IProgress that reports on the calling thread (Progress&lt;T&gt; would post).</summary>
    private sealed class SyncProgress(Action<(int Done, int Total)> report) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => report(value);
    }

    [Fact]
    public async Task FetchMissing_AfterARateLimit_AsksSteamNothingForFiveMinutes()
    {
        int searches = 0;
        using var http = new HttpClient(new FakeHandler(_ => { Interlocked.Increment(ref searches); return null; }));   // 429
        var time = new TestTime();
        var played = new[] { Played("x1", 100, PlayStatus.Mastered, name: "Control") };
        var source = new SteamTagSource(new CacheStore(_folder.Path), time);

        Assert.True(await source.FetchMissingAsync(played, [], new SteamTagClient(http)));
        time.Advance(TimeSpan.FromMinutes(4));
        // Steam asked nothing: the caller has nothing to compute again.
        Assert.False(await source.FetchMissingAsync(played, [], new SteamTagClient(http)));
        Assert.Equal(1, searches);

        // The pause is over: Steam is asked again.
        time.Advance(TimeSpan.FromMinutes(2));
        await source.FetchMissingAsync(played, [], new SteamTagClient(http));
        Assert.Equal(2, searches);
    }

    [Fact]
    public async Task FetchMissing_AfterTheStoreFails_AsksAgainAtOnce()
    {
        // Down (503), not a rate limit: no pause, the next computation tries again.
        int searches = 0;
        using var http = new HttpClient(new StatusHandler(HttpStatusCode.ServiceUnavailable, () => Interlocked.Increment(ref searches)));
        var played = new[] { Played("x1", 100, PlayStatus.Mastered, name: "Control") };
        var source = new SteamTagSource(new CacheStore(_folder.Path));

        await source.FetchMissingAsync(played, [], new SteamTagClient(http));
        await source.FetchMissingAsync(played, [], new SteamTagClient(http));

        Assert.Equal(2, searches);
    }

    /// <summary>A clock the test moves.</summary>
    private sealed class TestTime : TimeProvider
    {
        private DateTimeOffset _now = Now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Answers every request with this status, counting them.</summary>
    private sealed class StatusHandler(HttpStatusCode status, Action counted) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            counted();
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static bool IsControl(Uri uri) => Uri.UnescapeDataString(uri.Query).Contains("Control");

    /// <summary>
    /// A search that fails once <paramref name="kept"/> holds (30 s at most): searches go two at a
    /// time, so "the second one is refused" means "after the first result is kept".
    /// </summary>
    private static string? RefusedOnce(Func<bool> kept)
    {
        SpinWait.SpinUntil(kept, TimeSpan.FromSeconds(30));
        return null;
    }

    /// <summary>Answers each request with the given JSON, or 429 for null.</summary>
    private sealed class FakeHandler(Func<Uri, string?> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? json = answer(request.RequestUri!);
            return Task.FromResult(json is null
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : Json(json));
        }
    }
}
