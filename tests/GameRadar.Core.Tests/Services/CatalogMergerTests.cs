using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Services;

public class CatalogMergerTests
{
    [Fact]
    public void Combine_KeepsGamePassEntryOfAGameSoldOnSteamToo()
    {
        var onGamePass = Catalog("9ABC", title: "Warhammer 40,000: Space Marine 2 - Édition Standard", genres: ["Shooter"]);
        var onSteam = Catalog("steam:2183900", title: "Warhammer 40,000: Space Marine 2", genres: ["Action & adventure"],
            steamAppId: "2183900", inGamePass: false);

        var combined = CatalogMerger.Combine([onGamePass], [onSteam]);

        // One entry: the Game Pass one (its genres), now also known on Steam.
        var game = Assert.Single(combined);
        Assert.Equal("9ABC", game.ProductId);
        Assert.Equal(["Shooter"], game.Genres);
        Assert.Equal("2183900", game.SteamAppId);
        Assert.True(game.InGamePass);
    }

    [Fact]
    public void Combine_RecognizesAnEditionNameAsTheSameGame()
    {
        var onGamePass = Catalog("9DOOM", title: "DOOM Eternal Standard Edition");
        var onSteam = Catalog("steam:782330", title: "DOOM Eternal", steamAppId: "782330", inGamePass: false)
            with { Tags = new Dictionary<int, double> { [1663] = 1.0 } };

        var game = Assert.Single(CatalogMerger.Combine([onGamePass], [onSteam]));

        // The Game Pass entry takes the Steam tags too, for the similarity search.
        Assert.Equal("782330", game.SteamAppId);
        Assert.Equal(onSteam.Tags, game.Tags);
    }

    [Fact]
    public void Combine_LeavesOutGamePassGamesOfAHigherPlanUnlessSoldOnSteam()
    {
        // As a Premium subscriber sees them: both are Ultimate only.
        var ultimateOnly = Catalog("9ULT", title: "Ultimate Only") with { ConsoleTiers = GamePassTier.Ultimate };
        var ultimateAndSteam = Catalog("9UBI", title: "Far Cry 3") with { ConsoleTiers = GamePassTier.Ultimate };
        var onSteam = Catalog("steam:220240", title: "Far Cry 3", steamAppId: "220240", inGamePass: false, rating: 4.5, ratingCount: 90_000);

        var combined = CatalogMerger.Combine(
            [ultimateOnly.ForPlan(GamePassTier.Premium), ultimateAndSteam.ForPlan(GamePassTier.Premium)], [onSteam]);

        // Only the Steam entry, with its Steam rating: not a Game Pass game for this player.
        var game = Assert.Single(combined);
        Assert.Equal("steam:220240", game.ProductId);
        Assert.Equal(CatalogSource.Steam, game.PrimaryStore);
    }

    [Fact]
    public void Combine_AddsSteamGamesMissingFromGamePass()
    {
        var onGamePass = Catalog("9ABC", title: "Halo Infinite");
        var steamOnly = Catalog("steam:1245620", title: "ELDEN RING", steamAppId: "1245620", inGamePass: false);

        var combined = CatalogMerger.Combine([onGamePass], [steamOnly]);

        Assert.Equal(["9ABC", "steam:1245620"], combined.Select(g => g.ProductId));
        Assert.Equal(CatalogSource.Steam, combined[1].PrimaryStore);
        Assert.Null(combined[0].SteamAppId);
    }
    // A PS Plus entry as the player's plan sees it (ForPsPlusPlan), like the Game Pass one.
    private static CatalogGame PsPlus(string id, string title, PsPlusTier tiers = PsPlusTier.Extra | PsPlusTier.Premium) =>
        Catalog($"psplus:{id}", title: title, genres: ["Action & adventure"], inGamePass: false) with { PsPlusTiers = tiers };

    [Fact]
    public void Combine_PutsPsPlusGamesBetweenGamePassAndSteam()
    {
        var combined = CatalogMerger.Combine(
            [Catalog("9ABC", title: "Halo Infinite")],
            [Catalog("steam:1245620", title: "ELDEN RING", steamAppId: "1245620", inGamePass: false)],
            [PsPlus("1", "Horizon Zero Dawn")]);

        Assert.Equal(["9ABC", "psplus:1", "steam:1245620"], combined.Select(g => g.ProductId));
    }

    [Fact]
    public void Combine_KeepsTheGamePassEntryOfAGameAlsoInPsPlus()
    {
        var onGamePass = Catalog("9ABC", title: "Hades", genres: ["Action & adventure"]);
        var onPsPlus = PsPlus("5", "Hades PS4 & PS5", PsPlusTier.Premium) with { PlayStationConceptId = "5" };

        var game = Assert.Single(CatalogMerger.Combine([onGamePass], [], [onPsPlus]));

        // One card, the Game Pass one, which also notes the PS Plus plans.
        Assert.Equal("9ABC", game.ProductId);
        Assert.True(game.InGamePass);
        Assert.Equal(PsPlusTier.Premium, game.PsPlusTiers);
        // And where to open it on the PlayStation store.
        Assert.Equal("5", game.PlayStationConceptId);
    }

    [Fact]
    public void Combine_GivesAPsPlusGameSoldOnSteamItsSteamTagsAndRating()
    {
        var onPsPlus = PsPlus("2", "Dead Cells PS4 & PS5");
        var onSteam = Catalog("steam:588650", title: "Dead Cells", steamAppId: "588650", inGamePass: false,
            rating: 4.7, ratingCount: 120_000, modes: PlayModes.Solo)
            with { Tags = new Dictionary<int, double> { [1100] = 1.0 } };

        var game = Assert.Single(CatalogMerger.Combine([], [onSteam], [onPsPlus]));

        // The PS Plus entry (the subscription is the point), with what Steam knows of the game.
        Assert.Equal("psplus:2", game.ProductId);
        Assert.True(game.InPsPlus);
        Assert.Equal("588650", game.SteamAppId);
        Assert.Equal(onSteam.Tags, game.Tags);
        Assert.Equal(4.7, game.AverageRating);
        Assert.Equal(120_000, game.RatingCount);
        Assert.Equal(PlayModes.Solo, game.Modes);
        // Its own genres stay (the PlayStation ones, in the common vocabulary).
        Assert.Equal(["Action & adventure"], game.Genres);
    }

    [Fact]
    public void Combine_ReturnsAHigherPlanPsPlusGameAsASteamGameOnly()
    {
        // As an Extra subscriber sees it: a Classics game is no longer in PS Plus.
        var classic = PsPlus("3", "Tekken 3", PsPlusTier.Premium).ForPsPlusPlan(PsPlusTier.Extra);
        var onSteam = Catalog("steam:1", title: "Tekken 3", steamAppId: "1", inGamePass: false);

        var game = Assert.Single(CatalogMerger.Combine([], [onSteam], [classic]));

        Assert.Equal("steam:1", game.ProductId);
        Assert.False(game.InPsPlus);
    }

    [Fact]
    public void Combine_LeavesOutAHigherPlanPsPlusGameNobodyElseSells()
    {
        var classic = PsPlus("3", "Tekken 3", PsPlusTier.Premium).ForPsPlusPlan(PsPlusTier.Extra);

        Assert.Empty(CatalogMerger.Combine([], [], [classic]));
    }

    private static CatalogGame Gog(string id, string title) =>
        Catalog("gog:" + id, title: title, genres: ["Strategy"], inGamePass: false) with { GogStoreLink = "https://www.gog.com/en/game/" + id };

    [Fact]
    public void Combine_AddsTheGamesOnlyGogSells()
    {
        var game = Assert.Single(CatalogMerger.Combine([], [], gog: [Gog("1", "Disco Elysium")]));

        Assert.Equal("gog:1", game.ProductId);
        Assert.Equal(CatalogSource.Gog, game.PrimaryStore);
    }

    [Fact]
    public void Combine_MarksASteamGameSoldOnGogToo()
    {
        var onSteam = Catalog("steam:1091500", title: "Cyberpunk 2077", steamAppId: "1091500", inGamePass: false);

        var game = Assert.Single(CatalogMerger.Combine([], [onSteam], gog: [Gog("2", "Cyberpunk 2077®")]));

        // One entry, the Steam one (its tags, its rating), with both links.
        Assert.Equal("steam:1091500", game.ProductId);
        Assert.Equal("https://www.gog.com/en/game/2", game.GogStoreLink);
        Assert.Equal(CatalogSource.Steam, game.PrimaryStore);
    }

    [Fact]
    public void Combine_MarksAGamePassGameSoldOnGogToo()
    {
        var onGamePass = Catalog("9ABC", title: "The Witcher 3: Wild Hunt - Complete Edition");

        var game = Assert.Single(CatalogMerger.Combine([onGamePass], [], gog: [Gog("3", "The Witcher 3: Wild Hunt")]));

        Assert.Equal("9ABC", game.ProductId);
        Assert.Equal("https://www.gog.com/en/game/3", game.GogStoreLink);
        Assert.True(game.InGamePass);
    }

    [Fact]
    public void Combine_KeepsTheFirstGogEntryOfATitle()
    {
        // Best seller first: the second one is another edition of the same game.
        var combined = CatalogMerger.Combine([], [], gog: [Gog("4", "Baldur's Gate 3"), Gog("5", "Baldur's Gate 3 - Digital Deluxe Edition")]);

        Assert.Equal("gog:4", Assert.Single(combined).ProductId);
    }

    private static CatalogGame Epic(string ns, string title) =>
        Catalog("epic:" + ns, title: title, genres: ["Action & adventure"], inGamePass: false) with { EpicStoreLink = "https://store.epicgames.com/fr/p/" + ns };

    [Fact]
    public void Combine_AddsTheGamesOnlyEpicSellsLast()
    {
        var combined = CatalogMerger.Combine([], [], gog: [Gog("1", "Disco Elysium")], epic: [Epic("aw2", "Alan Wake 2")]);

        Assert.Equal(["gog:1", "epic:aw2"], combined.Select(g => g.ProductId));
        Assert.Equal(CatalogSource.Epic, combined[1].PrimaryStore);
    }

    [Fact]
    public void Combine_MarksAGameSoldOnSteamOrGogAndOnEpic()
    {
        var onSteam = Catalog("steam:3", title: "Hades", genres: ["Action & adventure"], inGamePass: false) with { SteamAppId = "3" };

        var combined = CatalogMerger.Combine([], [onSteam], gog: [Gog("6", "Cyberpunk 2077")],
            epic: [Epic("hades", "Hades"), Epic("cp", "Cyberpunk 2077")]);

        // Each keeps its entry (Steam, GOG) and gains its Epic page.
        Assert.Equal(["steam:3", "gog:6"], combined.Select(g => g.ProductId));
        Assert.All(combined, g => Assert.NotNull(g.EpicStoreLink));
        Assert.Equal(CatalogSource.Gog, combined[1].PrimaryStore);
    }

    private static CatalogGame PsStore(string id, string title) =>
        Catalog("psstore:" + id, title: title, genres: ["Action & adventure"], inGamePass: false) with { PlayStationProductId = id };

    [Fact]
    public void Combine_AddsTheGamesOnlyThePlayStationStoreSellsLast()
    {
        var combined = CatalogMerger.Combine([], [], epic: [Epic("aw2", "Alan Wake 2")],
            psStore: [PsStore("EP-ASTRO", "ASTRO BOT™"), PsStore("EP-ASTRO-DELUXE", "ASTRO BOT Digital Deluxe Edition")]);

        // The first entry of a title wins (best seller first).
        Assert.Equal(["epic:aw2", "psstore:EP-ASTRO"], combined.Select(g => g.ProductId));
        Assert.Equal(CatalogSource.PlayStationStore, combined[1].PrimaryStore);
    }

    [Fact]
    public void Combine_MarksAGameOfAnotherSourceSoldOnThePlayStationStore()
    {
        var inPsPlus = Catalog("psplus:1", title: "Ghost of Tsushima", inGamePass: false) with { PsPlusTiers = PsPlusTier.Extra };

        var combined = CatalogMerger.Combine([], [], psPlus: [inPsPlus], epic: [Epic("aw2", "Alan Wake 2")],
            psStore: [PsStore("EP-GOT", "Ghost of Tsushima"), PsStore("EP-AW2", "Alan Wake 2 PS5")]);

        // Each keeps its entry (PS Plus, Epic) and gains its store product.
        Assert.Equal(["psplus:1", "epic:aw2"], combined.Select(g => g.ProductId));
        Assert.Equal(["EP-GOT", "EP-AW2"], combined.Select(g => g.PlayStationProductId));
        Assert.True(combined[0].InPsPlus);
        Assert.Equal(CatalogSource.PsPlus, combined[0].PrimaryStore);
        Assert.Equal(CatalogSource.Epic, combined[1].PrimaryStore);
    }
}
