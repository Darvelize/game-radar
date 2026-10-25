using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

/// <summary>
/// "Mes jeux": the player's games not finished yet nor counted in the taste
/// (the user's choice, 19/10/2026), one per title, as candidates.
/// </summary>
public class OwnedGamesTests
{
    private static readonly Func<PlayedGame, bool> CountsNone = _ => false;

    private static IReadOnlyList<CatalogGame> AddTo(IReadOnlyList<CatalogGame> pool, Func<PlayedGame, bool> counts, params PlayedGame[] history) =>
        OwnedGames.AddTo(pool, OwnedGames.Collect(history, counts), []);

    [Fact]
    public void AddTo_ANeverLaunchedSteamGame_IsACandidateWithItsAppIdAndPicture()
    {
        var game = Assert.Single(AddTo([], CountsNone, NeverLaunched("620", "Portal 2")));

        Assert.Equal("mine:portal2", game.ProductId);
        Assert.Equal("Portal 2", game.Title);
        Assert.Equal("620", game.SteamAppId);
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/620/header.jpg", game.ImageUrl);
        Assert.True(game.IsIn(CatalogSource.MyGames));
        Assert.Equal(["Steam"], game.Owned!.Platforms);
        Assert.Equal(0, game.Owned.PlaytimeMinutes);
        Assert.Null(game.Owned.AchievementPercent);
        Assert.True(game.Owned.NeverPlayed);
    }

    [Fact]
    public void AddTo_OneCardPerGame_AcrossPlatforms()
    {
        var xbox = Played("1234", 12, name: "Cyberpunk 2077") with { PlaytimeMinutes = null };
        var steam = Played("1091500", platform: "Steam", name: "Cyberpunk 2077™", hasAchievements: false) with { PlaytimeMinutes = 240 };

        var game = Assert.Single(AddTo([], CountsNone, xbox, steam));

        Assert.Equal(["Xbox", "Steam"], game.Owned!.Platforms);
        Assert.Equal(240, game.Owned.PlaytimeMinutes);
        Assert.Equal(12, game.Owned.AchievementPercent);
        Assert.False(game.Owned.NeverPlayed);
        Assert.Equal("1091500", game.SteamAppId);
    }

    [Theory]
    [InlineData(PlayStatus.Completed)]
    [InlineData(PlayStatus.Mastered)]
    public void AddTo_LeavesOutAGameFinishedOnAnyPlatform(PlayStatus status)
    {
        var started = Played("1", 10, name: "Halo 5");
        var finished = Played("2", 100, status, platform: "Steam", name: "Halo 5");

        Assert.Empty(AddTo([], CountsNone, started, finished));
    }

    [Fact]
    public void AddTo_LeavesOutAGameThatCountsInTheTaste()
    {
        // The heart of the history: a game is never recommended because of itself.
        var liked = Played("1", 40, name: "Destiny 2");
        var forgotten = Played("2", 1, name: "Rogue Waters");

        var result = AddTo([], g => g == liked, liked, forgotten);

        Assert.Equal(["Rogue Waters"], result.Select(g => g.Title));
    }

    [Fact]
    public void AddTo_MarksTheCatalogsEntryOfTheSameGame()
    {
        // In the Game Pass: one card, with its badges and rating, owned.
        var entry = Catalog("9P1", "Cyberpunk 2077: Ultimate Edition", genres: ["Role playing"]) with { AverageRating = 4.5, RatingCount = 900 };
        var other = Catalog("9P2", "Halo Infinite");

        var result = AddTo([entry, other], CountsNone, Played("1234", 12, name: "Cyberpunk 2077"));

        Assert.Equal(2, result.Count);
        var owned = result.Single(g => g.Owned is not null);
        Assert.Equal("9P1", owned.ProductId);
        Assert.Equal(4.5, owned.AverageRating);
        Assert.True(owned.IsIn(CatalogSource.GamePass));
        Assert.True(owned.IsIn(CatalogSource.MyGames));
        Assert.Null(result.Single(g => g.ProductId == "9P2").Owned);
    }

    [Fact]
    public void AddTo_MarksTheCatalogsEntryWithTheSameSteamAppId()
    {
        // The Steam catalog sells "Portal 2: Complete" under the app id of the game owned: one card.
        var entry = Catalog("steam:620", "Portal 2: Complete", steamAppId: "620", inGamePass: false);

        var game = Assert.Single(AddTo([entry], CountsNone, NeverLaunched("620", "Portal 2")));

        Assert.Equal("steam:620", game.ProductId);
        Assert.NotNull(game.Owned);
    }

    [Fact]
    public void AddTo_MarksTheCatalogsEntryWithTheSameXboxTitleId()
    {
        // The Game Pass sells "Sea of Thieves 2024 Edition" under the title id of the game played: one card.
        var entry = Catalog("9P1", "Sea of Thieves 2024 Edition", xboxTitleId: "1234");

        var game = Assert.Single(AddTo([entry], CountsNone, Played("1234", 12, name: "Sea of Thieves")));

        Assert.Equal("9P1", game.ProductId);
        Assert.NotNull(game.Owned);
    }

    [Fact]
    public void AddTo_AGameWithAchievementsButNoPlayTime_IsStarted()
    {
        // Played offline: Steam counted no minutes, the achievements tell.
        var game = NeverLaunched("620", "Portal 2") with { ProgressPercentage = 30, UnlockedAchievements = 15 };

        Assert.False(Assert.Single(AddTo([], CountsNone, game)).Owned!.NeverPlayed);
    }

    [Fact]
    public void Others_IsTheHistoryWithoutMyGames()
    {
        var started = Played("1", 3, name: "Rogue Waters");
        var finished = Played("2", 100, PlayStatus.Mastered, name: "Halo 5");

        Assert.Equal([finished], OwnedGames.Others([started, finished], OwnedGames.Collect([started, finished], CountsNone)));
    }

    [Fact]
    public void AddTo_TakesThePictureOfTheHistory_WithoutASteamAppId()
    {
        // Only in the Xbox history (left the Game Pass): its Xbox picture.
        var xbox = Played("2090925193", 0, name: "Metal Slug Tactics") with { ImageUrl = "https://store-images.s-microsoft.com/image/hero" };

        Assert.Equal("https://store-images.s-microsoft.com/image/hero", Assert.Single(AddTo([], CountsNone, xbox)).ImageUrl);
    }

    [Fact]
    public void AddTo_PrefersTheSteamPictureOfTheAppId()
    {
        var xbox = Played("1", 0, name: "Portal 2") with { ImageUrl = "https://store-images.s-microsoft.com/image/hero" };

        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/620/header.jpg",
            Assert.Single(AddTo([], CountsNone, xbox, NeverLaunched("620", "Portal 2"))).ImageUrl);
    }

    // ---------- A Game Pass subscriber: an Xbox game out of the Game Pass is not "theirs" ----------

    private static IReadOnlyList<string> PlayableTitles(IReadOnlyList<CatalogGame> pool, params PlayedGame[] history) =>
        OwnedGames.WithoutGamePassLeftovers(OwnedGames.Collect(history, CountsNone), pool).Values.Select(lines => lines[0].Name).ToList();

    [Fact]
    public void WithoutGamePassLeftovers_LeavesOutAnXboxGameOutOfTheGamePass()
    {
        // Played through the Game Pass, left it since (Metal Slug Tactics): no longer playable.
        var gone = Played("2090925193", 0, name: "Metal Slug Tactics");
        // Still in the plan, under another title: found by its Xbox title id.
        var kept = Played("1234", 3, name: "Avowed");
        var pool = new[] { Catalog("9P1", "Avowed Standard Edition", xboxTitleId: "1234") };

        Assert.Equal(["Avowed"], PlayableTitles(pool, gone, kept));
    }

    [Fact]
    public void WithoutGamePassLeftovers_KeepsAGameAlsoOnAnotherPlatform()
    {
        // Also in the Steam history: the player has it there.
        var xbox = Played("2090925193", 0, name: "Metal Slug Tactics");
        var steam = NeverLaunched("2376580", "Metal Slug Tactics");

        Assert.Equal(["Metal Slug Tactics"], PlayableTitles([], xbox, steam));
    }

    [Fact]
    public void WithoutGamePassLeftovers_AnXboxGameInAStoreButNotInTheGamePass_IsLeftOut()
    {
        // Sold on Steam, not in the plan: the player does not have it there.
        var pool = new[] { Catalog("steam:1", "Metal Slug Tactics", steamAppId: "1", inGamePass: false) };

        Assert.Empty(PlayableTitles(pool, Played("2090925193", 0, name: "Metal Slug Tactics")));
    }

    [Fact]
    public void AddTo_TakesTheGenresKnownFromTheHistory()
    {
        var game = Played("1", 3, name: "Rogue Waters");

        var result = OwnedGames.AddTo([], OwnedGames.Collect([game], CountsNone), [WithGenres(game, "Strategy")]);

        Assert.Equal(["Strategy"], Assert.Single(result).Genres);
    }

    [Fact]
    public void AddTo_WithoutHistory_LeavesThePoolAsItIs()
    {
        var pool = new[] { Catalog("9P1") };

        Assert.Equal(pool, AddTo(pool, CountsNone));
    }

    [Fact]
    public void MyGames_IsTheLastSource_AndAMineGamesPrimaryStore()
    {
        Assert.Equal(CatalogSource.MyGames, Enum.GetValues<CatalogSource>()[^1]);
        var mine = Assert.Single(AddTo([], CountsNone, Played("1", 3, name: "Rogue Waters")));
        Assert.Equal(CatalogSource.MyGames, mine.PrimaryStore);
    }
}
