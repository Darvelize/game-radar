using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

public class RecommendationEngineTests
{
    // A player who went far in shooters and dropped strategy games.
    private static readonly TasteProfile Profile = TasteProfile.Build(
    [
        WithGenres(Played("s1", 100, PlayStatus.Mastered), "Shooter"),
        WithGenres(Played("s2", 60), "Shooter"),
        WithGenres(Played("t1", 1), "Strategy"),
        WithGenres(Played("t2", 1), "Strategy")
    ], Now);

    private static IReadOnlyList<Recommendation> Recommend(IEnumerable<CatalogGame> catalog, params PlayedGame[] history) =>
        RecommendationEngine.Recommend(Profile, catalog.ToList(), history, new CatalogMatcher(catalog));

    [Fact]
    public void Recommend_RanksLikedGenreFirst()
    {
        var shooter = Catalog("P1", genres: ["Shooter"]);
        var strategy = Catalog("P2", genres: ["Strategy"]);

        var result = Recommend([strategy, shooter]);

        Assert.Same(shooter, result[0].Game);
        Assert.Same(strategy, result[1].Game);
    }

    // ---------- Steam tags (the user's choice: finer than the Store genres) ----------

    private const int OpenWorld = 1695, StoryRich = 1742, Roguelike = 1716, Action = 19;

    // Loves open worlds with a story, dropped the roguelikes; plays everything with action.
    private static readonly TasteProfile TagProfile = TasteProfile.Build(
    [
        Tagged(Played("o1", 100, PlayStatus.Mastered), (OpenWorld, 1), (StoryRich, 0.8), (Action, 0.6)),
        Tagged(Played("o2", 60), (OpenWorld, 1), (StoryRich, 1), (Action, 0.5)),
        Tagged(Played("r1", 1), (Roguelike, 1), (Action, 0.8)),
        Tagged(Played("r2", 1), (Roguelike, 1), (Action, 0.7))
    ], Now);

    private static PlayedGameWithGenres Tagged(PlayedGame game, params (int Tag, double Weight)[] tags) =>
        new(game, ["Action & adventure"], Tags: tags.ToDictionary(t => t.Tag, t => t.Weight));

    private static CatalogGame TaggedCatalog(string id, params (int Tag, double Weight)[] tags) =>
        Catalog(id, genres: ["Action & adventure"]) with { Tags = tags.ToDictionary(t => t.Tag, t => t.Weight) };

    [Fact]
    public void Recommend_ScoresOnSteamTagsWhenTheGameHasSome()
    {
        // Same Store genre for both: only their tags tell them apart.
        var openWorld = TaggedCatalog("P1", (OpenWorld, 1), (StoryRich, 0.7), (Action, 0.5));
        var roguelike = TaggedCatalog("P2", (Roguelike, 1), (Action, 0.9));
        var catalog = new[] { roguelike, openWorld };
        // Tag rarity as on a real catalog: "Action" everywhere, the others rare.
        var corpus = catalog.Select(g => g.Tags).Concat(Enumerable.Repeat<IReadOnlyDictionary<int, double>?>(
            new Dictionary<int, double> { [Action] = 1 }, 8));

        var result = RecommendationEngine.Recommend(TagProfile, catalog, [], new CatalogMatcher(catalog),
            tagVectorizer: new TagVectorizer(corpus));

        Assert.Same(openWorld, result[0].Game);
        Assert.Empty(result[0].ReasonGenres);
        // The explanation names the loved tags weighing most on this game.
        Assert.Equal(OpenWorld, result[0].ReasonTags![0]);
        Assert.DoesNotContain(Roguelike, result[1].ReasonTags!);
    }

    [Fact]
    public void Recommend_PutsAGameWithoutTagsAtItsRankOnTheTagScale()
    {
        // Genre and tag scores are not on the same scale (on real data, the
        // games without tags took the whole top). Same genre as every tagged
        // game here: it lands among them, not above the best tag match.
        var openWorld = TaggedCatalog("P1", (OpenWorld, 1), (StoryRich, 0.7), (Action, 0.5));
        var roguelike = TaggedCatalog("P2", (Roguelike, 1), (Action, 0.9));
        var middle = TaggedCatalog("P3", (OpenWorld, 0.5), (Roguelike, 1), (Action, 0.5));
        var exclusive = Catalog("P4", genres: ["Action & adventure"]);
        var catalog = new[] { roguelike, exclusive, middle, openWorld };
        var corpus = catalog.Select(g => g.Tags).Concat(Enumerable.Repeat<IReadOnlyDictionary<int, double>?>(
            new Dictionary<int, double> { [Action] = 1 }, 8));

        var result = RecommendationEngine.Recommend(TagProfile, catalog, [], new CatalogMatcher(catalog),
            tagVectorizer: new TagVectorizer(corpus));

        Assert.Equal(["P1", "P4", "P2"], result.Select(r => r.Game.ProductId).Where(id => id != "P3"));
    }

    [Fact]
    public void Recommend_LearnsTheTagScaleOnlyOnGamesScoredBothWays()
    {
        var openWorld = TaggedCatalog("P1", (OpenWorld, 1), (StoryRich, 0.7), (Action, 0.5));
        var roguelike = TaggedCatalog("P2", (Roguelike, 1), (Action, 0.9));
        var exclusive = Catalog("P4", genres: ["Action & adventure"]);
        // Tags but no Store genre: its "genre score" is no score, not a 0.
        var noGenre = Catalog("P5", genres: []) with { Tags = new Dictionary<int, double> { [StoryRich] = 1 } };
        var vectorizer = new TagVectorizer(new[] { openWorld.Tags, roguelike.Tags, noGenre.Tags }.Concat(
            Enumerable.Repeat<IReadOnlyDictionary<int, double>?>(new Dictionary<int, double> { [Action] = 1 }, 8)));
        double ScoreOfExclusive(params CatalogGame[] catalog) =>
            RecommendationEngine.Recommend(TagProfile, catalog, [], new CatalogMatcher(catalog), tagVectorizer: vectorizer)
                .Single(r => r.Game.ProductId == "P4").Score;

        Assert.Equal(ScoreOfExclusive(openWorld, roguelike, exclusive), ScoreOfExclusive(openWorld, roguelike, exclusive, noGenre), 9);
    }

    [Fact]
    public void Recommend_FallsBackOnGenresForAGameWithoutTags()
    {
        // Not on Steam (an Xbox exclusive): scored on its genres, as before.
        var exclusive = Catalog("P1", genres: ["Shooter"]);

        var result = RecommendationEngine.Recommend(Profile, [exclusive], [], new CatalogMatcher([exclusive]));

        Assert.Equal(["Shooter"], result[0].ReasonGenres);
        Assert.Null(result[0].ReasonTags);
    }

    [Fact]
    public void Recommend_KeepsGameOwnedOnSteamButNeverLaunched()
    {
        // The user's choice: a forgotten game of the library is worth a reminder.
        var backlog = Catalog("P1", title: "Backlog Game", genres: ["Shooter"]);
        var owned = Played("10", name: "Backlog Game", hasAchievements: false, platform: "Steam") with { PlaytimeMinutes = 0 };

        var result = Recommend([backlog], owned);

        Assert.Same(backlog, Assert.Single(result).Game);
    }

    [Fact]
    public void Recommend_ExcludesGamePlayedOnSteam()
    {
        var played = Catalog("P1", title: "Steam Hit", genres: ["Shooter"]);

        var result = Recommend([played], Played("10", name: "Steam Hit", platform: "Steam") with { PlaytimeMinutes = 600 });

        Assert.Empty(result);
    }

    [Theory]
    // Found by the calibration: the played game came back as another edition.
    [InlineData("Control", "Control Ultimate Edition - Xbox Series X|S")]
    [InlineData("Sleeping Dogs™", "Sleeping Dogs: Definitive Edition")]
    public void Recommend_ExcludesAnotherEditionOfAPlayedGame(string playedName, string catalogTitle)
    {
        var edition = Catalog("P1", title: catalogTitle, genres: ["Shooter"]);

        Assert.Empty(Recommend([edition], Played("x1", 60, name: playedName)));
    }

    [Fact]
    public void Recommend_ExcludesSteamCandidatePlayedUnderAnotherTitle()
    {
        // Same Steam app, title worded differently in the store and the library.
        var candidate = Catalog("steam:1091500", title: "Cyberpunk 2077: Ultimate", genres: ["Shooter"],
            steamAppId: "1091500", inGamePass: false);

        var result = Recommend([candidate], Played("1091500", name: "Cyberpunk 2077", platform: "Steam") with { PlaytimeMinutes = 600 });

        Assert.Empty(result);
    }

    [Fact]
    public void Recommend_ExcludesSteamCandidateAddedByHandUnderAnotherTitle()
    {
        var candidate = Catalog("steam:1145360", title: "Hades", genres: ["Shooter"], steamAppId: "1145360", inGamePass: false);
        // Played on Switch, picked from the Steam search, then renamed.
        var played = ManualGames.ToPlayed(new ManualGame("Hades (Switch)", ManualPlatform.Switch, ManualLevel.Completed, SteamAppId: "1145360"));

        Assert.Empty(Recommend([candidate], played));
    }

    [Fact]
    public void Recommend_DoesNotConfuseSteamAppIdWithXboxTitleId()
    {
        var xboxGame = Catalog("P1", title: "Some Xbox Game", xboxTitleId: "620", genres: ["Shooter"]);

        // Steam appid 620 is another game: only the name may match.
        var result = Recommend([xboxGame], Played("620", name: "Portal 2", platform: "Steam") with { PlaytimeMinutes = 600 });

        Assert.Same(xboxGame, Assert.Single(result).Game);
    }

    [Fact]
    public void Recommend_ExcludesPlayedGameAndItsOtherEditions()
    {
        var sameProduct = Catalog("P1", title: "Game X", xboxTitleId: "111", genres: ["Shooter"]);
        var otherEdition = Catalog("P2", title: "Game X - Pack Cross-gen", genres: ["Shooter"]);
        var notPlayed = Catalog("P3", title: "Game Y", genres: ["Shooter"]);

        var result = Recommend([sameProduct, otherEdition, notPlayed], Played("111", name: "Game X"));

        Assert.Same(notPlayed, Assert.Single(result).Game);
    }

    [Fact]
    public void Recommend_KeepsMyGames_WithTheHistoryLeftOnceTheyAreTakenOut()
    {
        // "Mes jeux": started, not finished, not in the taste; the player asked for it.
        var started = Played("111", 3, name: "Game X");
        var mine = Catalog("P1", title: "Game X", xboxTitleId: "111", genres: ["Shooter"]);
        var otherPlayed = Catalog("P2", title: "Game Y", genres: ["Shooter"]);
        var history = new[] { started, Played("222", 100, PlayStatus.Mastered, name: "Game Y") };
        var owned = OwnedGames.Collect(history, _ => false);

        var result = Recommend(OwnedGames.AddTo([mine, otherPlayed], owned, []), [.. OwnedGames.Others(history, owned)]);

        Assert.Equal("P1", Assert.Single(result).Game.ProductId);
    }

    [Fact]
    public void Recommend_LeavesOutOneOfMyGames_FinishedUnderAnotherTitle()
    {
        // Mastered on Xbox as "Halo MCC" (matched by its title id), owned never launched on Steam
        // under the catalog's title: finished, never offered.
        var catalogEntry = Catalog("P1", title: "Halo: The Master Chief Collection", xboxTitleId: "111", genres: ["Shooter"]);
        var history = new[]
        {
            Played("111", 100, PlayStatus.Mastered, name: "Halo MCC"),
            NeverLaunched("976730", "Halo: The Master Chief Collection")
        };
        var owned = OwnedGames.Collect(history, _ => false);

        Assert.Empty(Recommend(OwnedGames.AddTo([catalogEntry], owned, []), [.. OwnedGames.Others(history, owned)]));
    }

    [Fact]
    public void Recommend_KeepsOnlyMostRatedEdition()
    {
        var standard = Catalog("P1", title: "Game Z - Édition Standard", genres: ["Shooter"], rating: 4.5, ratingCount: 100);
        var crossGen = Catalog("P2", title: "Game Z - Pack Cross-gen", genres: ["Shooter"], rating: 4.5, ratingCount: 5000);

        var result = Recommend([standard, crossGen]);

        Assert.Same(crossGen, Assert.Single(result).Game);
    }

    [Fact]
    public void Recommend_KeepsHyphenInsideTitles()
    {
        // Only a spaced dash marks an edition: "Half" is not "Half-Life".
        var halfLife = Catalog("P1", title: "Half-Life", genres: ["Shooter"]);

        var result = Recommend([halfLife], Played("999", name: "Half"));

        Assert.Same(halfLife, Assert.Single(result).Game);
    }

    [Theory]
    [InlineData("The Elder Scrolls V: Skyrim Special Edition (PC)", "The Elder Scrolls V: Skyrim Special Edition")]
    [InlineData("Overwatch", "Overwatch® PC")]
    [InlineData("Battlefield 2042", "Battlefield™ 2042 sur Xbox Series X|S")]
    [InlineData("Tales of Arise (Windows)", "Tales of Arise")]
    public void Recommend_IgnoresPlatformSuffixWhenExcludingPlayedGames(string playedName, string catalogTitle)
    {
        var result = Recommend([Catalog("P1", title: catalogTitle, genres: ["Shooter"])], Played("999", name: playedName));

        Assert.Empty(result);
    }

    [Fact]
    public void Recommend_KeepsYearInParentheses()
    {
        // "(2008)" is not a platform: the original and the remake are two games.
        var original = Catalog("P1", title: "Dead Space (2008)", genres: ["Shooter"]);

        var result = Recommend([original], Played("999", name: "Dead Space"));

        Assert.Same(original, Assert.Single(result).Game);
    }

    [Fact]
    public void Recommend_KeepsOneOfSeveralPlatformVersions()
    {
        var seriesX = Catalog("P1", title: "Battlefield™ 2042 sur Xbox Series X|S", genres: ["Shooter"], ratingCount: 10);
        var xboxOne = Catalog("P2", title: "Battlefield™ 2042 sur Xbox One", genres: ["Shooter"], ratingCount: 99);

        Assert.Same(xboxOne, Assert.Single(Recommend([seriesX, xboxOne])).Game);
    }

    [Fact]
    public void Recommend_BreaksTiesWithSmoothedRating()
    {
        // 5/5 from 3 people must not beat 4.8/5 from 5,000.
        var fewRatings = Catalog("P1", genres: ["Shooter"], rating: 5.0, ratingCount: 3);
        var manyRatings = Catalog("P2", genres: ["Shooter"], rating: 4.8, ratingCount: 5000);

        var result = Recommend([fewRatings, manyRatings]);

        Assert.Same(manyRatings, result[0].Game);
    }

    [Fact]
    public void Recommend_SkipsGamesWithoutGenres()
    {
        Assert.Empty(Recommend([Catalog("P1", genres: [])]));
    }

    [Fact]
    public void Recommend_ExplainsWithStronglyPlayedGenresOnly()
    {
        var game = Catalog("P1", genres: ["Shooter", "Strategy"]);

        var result = Assert.Single(Recommend([game]));

        Assert.Equal(["Shooter"], result.ReasonGenres);
    }

    // ---------- Steam wishlist (the user's choice: a score bonus, nothing else changes) ----------

    private static IReadOnlyList<Recommendation> RecommendWithWishlist(
        IReadOnlyList<CatalogGame> catalog, IReadOnlySet<string>? wishlist, params PlayedGame[] history) =>
        RecommendationEngine.Recommend(Profile, catalog, history, new CatalogMatcher(catalog), wishlist: wishlist);

    [Fact]
    public void Recommend_PutsAWishlistedGameAheadOfAnEquivalentOne()
    {
        // Same genre, same rating: only the wishlist tells them apart.
        var plain = Catalog("P1", title: "Plain", genres: ["Shooter"]);
        var wished = Catalog("P2", title: "Wished", genres: ["Shooter"]) with { SteamAppId = "100" };

        var result = RecommendWithWishlist([plain, wished], new HashSet<string> { "100" });

        Assert.Same(wished, result[0].Game);
        Assert.True(result[0].Score > result[1].Score);
    }

    [Fact]
    public void Recommend_WithoutAWishlistScoresAsBefore()
    {
        var wished = Catalog("P2", title: "Wished", genres: ["Shooter"]) with { SteamAppId = "100" };

        double none = RecommendWithWishlist([wished], null)[0].Score;
        double empty = RecommendWithWishlist([wished], new HashSet<string>())[0].Score;
        double elsewhere = RecommendWithWishlist([wished], new HashSet<string> { "999" })[0].Score;

        Assert.Equal(none, empty);
        Assert.Equal(none, elsewhere);
    }

    [Fact]
    public void Recommend_AWishlistDoesNotOverrideAGapOfTaste()
    {
        // A strategy game is on the wishlist, but this player drops strategy games.
        var strategy = Catalog("P1", title: "Wished strategy", genres: ["Strategy"]) with { SteamAppId = "100" };
        var shooter = Catalog("P2", title: "Plain shooter", genres: ["Shooter"]);

        var result = RecommendWithWishlist([strategy, shooter], new HashSet<string> { "100" });

        Assert.Same(shooter, result[0].Game);
    }

    [Fact]
    public void Recommend_StillExcludesAWishlistedGameAlreadyPlayed()
    {
        var wished = Catalog("P1", title: "Wished", genres: ["Shooter"]) with { SteamAppId = "100" };
        var played = Played("100", 50) with { Platform = "Steam", SteamAppId = "100", Name = "Wished" };

        Assert.Empty(RecommendWithWishlist([wished], new HashSet<string> { "100" }, played));
    }

    [Fact]
    public void Recommend_ReturnsAtMostMaxCount()
    {
        var catalog = Enumerable.Range(0, 10).Select(i => Catalog($"P{i}", title: $"Game {i}", genres: ["Shooter"])).ToList();

        var result = RecommendationEngine.Recommend(Profile, catalog, [], new CatalogMatcher(catalog), maxCount: 3);

        Assert.Equal(3, result.Count);
    }
}
