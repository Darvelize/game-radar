using GameRadar.App.ViewModels;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.App.Tests;

public sealed class RecommendationRowTests
{
    private static RecommendationRow Row(CatalogGame game) =>
        new(new SimilarGame(new Recommendation(game, 0.5, []), 0.9, ["Shooter"], PlayModes.None));

    [Fact]
    public void A_card_shows_the_three_most_voted_tags_instead_of_the_genres()
    {
        var game = new CatalogGame("steam:230410", "Warframe", null, null, ["Shooter", "Role playing"],
            OnPc: false, OnConsole: false, LeavingSoon: false, SteamAppId: "230410",
            Tags: new Dictionary<int, double> { [1] = 1.0, [2] = 0.9, [3] = 0.8, [4] = 0.7 });
        var names = new Dictionary<int, string> { [1] = "Tir avec butin", [2] = "Action et RPG", [3] = "Tir à la 3ᵉ personne", [4] = "Action" };

        var row = new RecommendationRow(new SimilarGame(new Recommendation(game, 0.5, []), 0.9, ["Shooter"], PlayModes.None), names);

        Assert.Equal(["#Tir avec butin", "#Action et RPG", "#Tir à la 3ᵉ personne"], row.StyleTags);
        Assert.Equal("", row.StyleLabel);
    }

    // ---------- "Mes jeux": the player's own games not finished yet ----------

    private static CatalogGame Mine(OwnedGame owned, string? appId = null) =>
        new("mine:portal2", "Portal 2", null, null, ["Shooter"], OnPc: false, OnConsole: false, LeavingSoon: false, SteamAppId: appId)
        {
            Owned = owned
        };

    [Theory]
    [InlineData(true, 0, null, "Jamais lancé")]
    [InlineData(false, 240, 12.4, "Commencé : 4 h de jeu, 12 % des succès")]
    [InlineData(false, 240, null, "Commencé : 4 h de jeu")]
    [InlineData(false, null, 12.4, "Commencé : 12 % des succès")]
    [InlineData(false, null, null, "Commencé")]
    [InlineData(false, null, 99.6, "Commencé : 99 % des succès")]
    public void One_of_my_games_says_how_far_I_went(bool neverPlayed, int? minutes, double? percent, string expected)
    {
        var row = Row(Mine(new OwnedGame(["Steam"], minutes, percent, neverPlayed)));

        Assert.True(row.IsMine);
        Assert.Equal(expected, row.OwnedLabel);
    }

    [Fact]
    public void One_of_my_games_in_no_catalog_names_where_I_have_it()
    {
        var row = Row(Mine(new OwnedGame(["Xbox", "Manual"], null, 3, NeverPlayed: false)));

        Assert.Equal("Xbox · Ajouté à la main", row.PlatformsLabel);
        Assert.False(row.IsOnSteam);
    }

    [Fact]
    public void One_of_my_games_on_Steam_opens_its_Steam_page()
    {
        var row = Row(Mine(new OwnedGame(["Steam"], 0, null, NeverPlayed: true), appId: "620"));

        Assert.True(row.IsOnSteam);
        Assert.Equal("Steam", row.PlatformsLabel);
    }

    [Theory]
    // The Microsoft image server resizes on the fly; a Steam header comes at its size.
    [InlineData("https://store-images.s-microsoft.com/image/hero", "https://store-images.s-microsoft.com/image/hero?w=560&h=315&q=80")]
    [InlineData("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/620/header.jpg", "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/620/header.jpg")]
    public void One_of_my_games_in_no_catalog_shows_the_picture_of_its_history(string picture, string shown)
    {
        var row = Row(Mine(new OwnedGame(["Xbox"], null, 0, NeverPlayed: false)) with { ImageUrl = picture });

        Assert.Equal(shown, row.ImageUrl);
    }

    [Fact]
    public void A_game_of_a_catalog_is_not_one_of_my_games()
    {
        var row = Row(new CatalogGame("gp:1", "Halo", null, null, ["Shooter"], OnPc: true, OnConsole: true, LeavingSoon: false));

        Assert.False(row.IsMine);
        Assert.Equal("", row.OwnedLabel);
    }

    [Fact]
    public void A_card_without_tags_or_tag_names_shows_its_genres()
    {
        var game = new CatalogGame("gp:1", "Halo", null, null, ["Shooter", "Action & adventure"],
            OnPc: true, OnConsole: true, LeavingSoon: false, Tags: new Dictionary<int, double> { [1] = 1.0 });

        // The tag names not known yet (first launch offline): the genres.
        Assert.Equal("Tir · Action-aventure", Row(game).StyleLabel);
        Assert.Empty(Row(game).StyleTags);
    }

    [Fact]
    public void Steam_only_game_shows_its_Steam_reviews_and_picture_as_is()
    {
        var game = new CatalogGame("steam:397540", "Borderlands 3", null, null, ["Shooter"],
            OnPc: false, OnConsole: false, LeavingSoon: false, AverageRating: 4.4, RatingCount: 187_500,
            ImageUrl: "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/397540/capsule_616x353.jpg?t=1",
            SteamAppId: "397540");

        var row = Row(game);

        Assert.False(row.InGamePass);
        Assert.True(row.IsOnSteam);
        Assert.Equal("88 %", row.RatingValueLabel);
        Assert.Equal("PC (Steam)", row.PlatformsLabel);
        // The Steam CDN does not resize: the address is kept untouched.
        Assert.Equal(game.ImageUrl, row.ImageUrl);
    }

    [Fact]
    public void GOG_only_game_shows_its_GOG_reviews_and_opens_its_GOG_page()
    {
        var game = new CatalogGame("gog:1207658787", "Disco Elysium", null, null, ["Role playing"],
            OnPc: false, OnConsole: false, LeavingSoon: false, AverageRating: 4.6, RatingCount: 12_345,
            ImageUrl: "https://images.gog-statics.com/abc_product_card_v2_mobile_slider_639.jpg",
            GogStoreLink: "https://www.gog.com/en/game/disco_elysium");

        var row = Row(game);

        Assert.True(row.IsOnGog);
        Assert.Equal(new Uri("https://www.gog.com/en/game/disco_elysium"), row.GogPage);
        Assert.False(row.IsOnSteam);
        Assert.Equal("92 %", row.RatingValueLabel);
        Assert.Equal("12 345 avis GOG", row.RatingCountLabel.Replace(' ', ' ').Replace(' ', ' '));
        Assert.Equal("PC (GOG)", row.PlatformsLabel);
        // Already GOG's own card size: no resizing parameters added.
        Assert.Equal(game.ImageUrl, row.ImageUrl);
    }

    [Theory]
    [InlineData("http://www.gog.com/en/game/x")]
    [InlineData("https://www.gog.com.evil.example/en/game/x")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("not a link")]
    public void A_GOG_link_that_is_not_an_https_gog_page_is_never_opened(string link)
    {
        var game = new CatalogGame("gog:1", "Un jeu", null, null, ["Shooter"],
            OnPc: false, OnConsole: false, LeavingSoon: false, GogStoreLink: link);

        Assert.Null(Row(game).GogPage);
    }

    [Fact]
    public void Steam_game_sold_on_GOG_too_shows_both_badges_and_the_Steam_reviews()
    {
        var game = new CatalogGame("steam:1091500", "Cyberpunk 2077", null, null, ["Role playing"],
            OnPc: false, OnConsole: false, LeavingSoon: false, AverageRating: 4.0, RatingCount: 700_000,
            SteamAppId: "1091500", GogStoreLink: "https://www.gog.com/en/game/cyberpunk_2077");

        var row = Row(game);

        Assert.True(row.IsOnSteam);
        Assert.True(row.IsOnGog);
        Assert.Contains("Steam", row.RatingCountLabel);
        Assert.Equal("PC (Steam)", row.PlatformsLabel);
    }

    [Fact]
    public void Epic_only_game_shows_its_players_rating_out_of_5_and_opens_its_Epic_page()
    {
        var game = new CatalogGame("epic:ns-aw2", "Alan Wake 2", null, null, ["Action & adventure"],
            OnPc: false, OnConsole: false, LeavingSoon: false, AverageRating: 4.8,
            ImageUrl: "https://cdn1.epicgames.com/aw2-wide.jpg",
            EpicStoreLink: "https://store.epicgames.com/p/alan-wake-2");

        var row = Row(game);

        Assert.True(row.IsOnEpic);
        Assert.Equal(new Uri("https://store.epicgames.com/fr/p/alan-wake-2"), row.EpicPage);
        // An average of 5 stars, not a share of positive reviews; no review count from Epic.
        Assert.Equal("4,8", row.RatingValueLabel);
        Assert.Equal("note des joueurs Epic", row.RatingCountLabel);
        Assert.Equal("PC (Epic)", row.PlatformsLabel);
        // Epic's CDN resizes with its own parameters (946 KB → 49 KB, measured).
        Assert.Equal(game.ImageUrl + "?resize=1&w=640&h=360&quality=medium", row.ImageUrl);
    }

    [Theory]
    // Its badges say PlayStation and the PC store: the line says both, the console first.
    [InlineData(null, "https://store.epicgames.com/p/alan-wake-2", "PlayStation et PC (Epic)")]
    [InlineData("https://www.gog.com/en/game/alan_wake_2", null, "PlayStation et PC (GOG)")]
    public void PC_store_game_also_on_the_PlayStation_Store_names_both_platforms(string? gogLink, string? epicLink, string expected)
    {
        var game = new CatalogGame("epic:ns-aw2", "Alan Wake 2", null, null, ["Action & adventure"],
            OnPc: false, OnConsole: false, LeavingSoon: false, AverageRating: 4.8,
            GogStoreLink: gogLink, EpicStoreLink: epicLink, PlayStationProductId: "EP0001-PPSA00001_00-AW2");

        var row = Row(game);

        Assert.True(row.IsOnPsStore);
        Assert.Equal(expected, row.PlatformsLabel);
    }

    [Theory]
    // In the player's PS Plus and sold on a PC store other than Steam: both platforms too.
    [InlineData("https://www.gog.com/en/game/hat", null, "PlayStation et PC (GOG)")]
    [InlineData(null, "https://store.epicgames.com/p/hat", "PlayStation et PC (Epic)")]
    public void PS_Plus_game_sold_on_a_PC_store_names_both_platforms(string? gogLink, string? epicLink, string expected)
    {
        var game = new CatalogGame("psplus:228903", "A Hat in Time", null, null, ["Action & adventure"],
            OnPc: false, OnConsole: false, LeavingSoon: false,
            PsPlusTiers: PsPlusTier.Extra, PlayStationConceptId: "228903", GogStoreLink: gogLink, EpicStoreLink: epicLink);

        Assert.Equal(expected, Row(game).PlatformsLabel);
    }

    [Fact]
    public void PlayStation_Store_only_game_shows_its_players_rating_out_of_5_and_opens_its_store_page()
    {
        var game = new CatalogGame("psstore:EP9000-PPSA01411_00-0000000000000000", "Ghost of Tsushima", null, null, ["Action & adventure"],
            OnPc: false, OnConsole: false, LeavingSoon: false, AverageRating: 4.6,
            ImageUrl: "https://image.api.playstation.com/vulcan/got.jpg",
            PlayStationProductId: "EP9000-PPSA01411_00-0000000000000000");

        var row = Row(game);

        Assert.True(row.IsOnPsStore);
        Assert.False(row.InPsPlus);
        // In the language of the app (the tests run in French).
        Assert.Equal(new Uri("https://store.playstation.com/fr-fr/product/EP9000-PPSA01411_00-0000000000000000"), row.PsStorePage);
        Assert.Equal("4,6", row.RatingValueLabel);
        Assert.Equal("note des joueurs PlayStation", row.RatingCountLabel);
        Assert.Equal("PlayStation", row.PlatformsLabel);
        // The PlayStation image server resizes, as for PS Plus.
        Assert.Equal(game.ImageUrl + "?w=560&h=315&q=80", row.ImageUrl);
    }

    [Fact]
    public void A_game_of_the_players_PS_Plus_keeps_its_PS_Plus_badge_only()
    {
        var game = new CatalogGame("psplus:1", "Ghost of Tsushima", null, null, ["Action & adventure"],
            OnPc: false, OnConsole: false, LeavingSoon: false, PsPlusTiers: PsPlusTier.Extra, PlayStationConceptId: "10000235",
            PlayStationProductId: "EP9000-PPSA01411_00-0000000000000000");

        var row = Row(game);

        Assert.True(row.InPsPlus);
        Assert.False(row.IsOnPsStore);
    }

    [Fact]
    public void A_Steam_game_also_on_the_PlayStation_Store_shows_both()
    {
        var game = new CatalogGame("steam:1", "Hades", null, null, ["Action & adventure"],
            OnPc: false, OnConsole: false, LeavingSoon: false, SteamAppId: "1145360",
            PlayStationProductId: "EP0000-PPSA00000_00-HADES00000000000");

        var row = Row(game);

        Assert.True(row.IsOnSteam);
        Assert.True(row.IsOnPsStore);
        Assert.Equal("PlayStation et PC (Steam)", row.PlatformsLabel);
    }

    [Fact]
    public void An_Epic_link_without_language_opens_in_the_language_of_the_app()
    {
        // The catalog keeps the link without language; the tests run in French.
        var game = new CatalogGame("epic:1", "Alan Wake 2", null, null, ["Shooter"],
            OnPc: false, OnConsole: false, LeavingSoon: false, EpicStoreLink: "https://store.epicgames.com/p/alan-wake-2");

        Assert.Equal(new Uri("https://store.epicgames.com/fr/p/alan-wake-2"), Row(game).EpicPage);
    }

    [Theory]
    [InlineData("http://store.epicgames.com/fr/p/x")]
    [InlineData("https://store.epicgames.com.evil.example/fr/p/x")]
    [InlineData("https://www.epicgames.com/fr/p/x")]
    [InlineData("not a link")]
    public void An_Epic_link_that_is_not_an_https_Epic_store_page_is_never_opened(string link)
    {
        var game = new CatalogGame("epic:1", "Un jeu", null, null, ["Shooter"],
            OnPc: false, OnConsole: false, LeavingSoon: false, EpicStoreLink: link);

        Assert.Null(Row(game).EpicPage);
    }

    [Fact]
    public void GOG_game_sold_on_Epic_too_shows_both_badges_and_the_GOG_reviews()
    {
        var game = new CatalogGame("gog:1", "Cyberpunk 2077", null, null, ["Role playing"],
            OnPc: false, OnConsole: false, LeavingSoon: false, AverageRating: 4.0, RatingCount: 9_000,
            GogStoreLink: "https://www.gog.com/en/game/cyberpunk_2077", EpicStoreLink: "https://store.epicgames.com/fr/p/cyberpunk-2077");

        var row = Row(game);

        Assert.True(row.IsOnGog);
        Assert.True(row.IsOnEpic);
        Assert.Contains("GOG", row.RatingCountLabel);
        Assert.Equal("PC (GOG)", row.PlatformsLabel);
    }

    private const string PlayStationPicture = "https://image.api.playstation.com/cdn/EP3717/CUSA10028_00/hat.png";

    [Fact]
    public void PS_Plus_game_opens_its_PlayStation_store_page_and_names_the_platform()
    {
        var game = new CatalogGame("psplus:228903", "A Hat in Time", null, null, ["Action & adventure"],
            OnPc: false, OnConsole: false, LeavingSoon: false, ImageUrl: PlayStationPicture,
            PsPlusTiers: PsPlusTier.Extra | PsPlusTier.Premium, PlayStationConceptId: "228903");

        var row = Row(game);

        Assert.True(row.InPsPlus);
        Assert.False(row.InGamePass);
        Assert.False(row.IsOnSteam);
        Assert.Equal(new Uri("https://store.playstation.com/fr-fr/concept/228903"), row.PsPlusPage);
        Assert.Equal("PlayStation", row.PlatformsLabel);
        // The PlayStation picture is a 1024 px square: the server shrinks it for the card, like the Microsoft one.
        Assert.StartsWith(PlayStationPicture + "?", row.ImageUrl);
    }

    [Fact]
    public void PS_Plus_game_sold_on_Steam_keeps_both_badges_and_the_Steam_reviews()
    {
        var game = new CatalogGame("psplus:2", "Dead Cells", null, null, ["Action & adventure"],
            OnPc: false, OnConsole: false, LeavingSoon: false, AverageRating: 4.7, RatingCount: 120_000,
            ImageUrl: PlayStationPicture, SteamAppId: "588650",
            PsPlusTiers: PsPlusTier.Extra | PsPlusTier.Premium, PlayStationConceptId: "2");

        var row = Row(game);

        Assert.True(row.InPsPlus && row.IsOnSteam);
        Assert.Equal("94 %", row.RatingValueLabel);
        Assert.Equal("PlayStation et PC (Steam)", row.PlatformsLabel);
        // Not a Steam picture: still shrunk.
        Assert.StartsWith(PlayStationPicture + "?", row.ImageUrl);
    }

    [Fact]
    public void Game_Pass_game_also_in_PS_Plus_shows_both_badges()
    {
        var game = new CatalogGame("9HADES", "Hades", null, null, ["Action & adventure"],
            OnPc: true, OnConsole: true, LeavingSoon: false,
            PsPlusTiers: PsPlusTier.Premium, PlayStationConceptId: "5");

        var row = Row(game);

        Assert.True(row.InGamePass && row.InPsPlus);
        Assert.Equal("PC et console", row.PlatformsLabel);
    }

    [Fact]
    public void Tag_match_is_explained_with_the_shared_tags_in_French()
    {
        var game = new CatalogGame("9DIV", "The Division 2", null, null, ["Shooter", "Role playing"],
            OnPc: true, OnConsole: true, LeavingSoon: false);
        var similar = new SimilarGame(new Recommendation(game, 0.5, []), 0.67, ["Shooter"], PlayModes.CoopOnline,
            ByTags: true, SharedTags: [353880, 3814, 999]);
        var names = new Dictionary<int, string> { [353880] = "Tir avec butin", [3814] = "Tir à la 3ᵉ personne" };

        var row = new RecommendationRow(similar, names);

        // A tag without a French name (999) is left out rather than shown as a number.
        Assert.Equal("Même style : Tir avec butin · Tir à la 3ᵉ personne", row.ReasonLabel);
    }

    [Fact]
    public void Taste_pick_is_explained_with_the_loved_tags_and_their_games()
    {
        const int openWorld = 1695, storyRich = 1742;
        var profile = TasteProfile.Build(
        [
            new PlayedGameWithGenres(Played("1"), [], Tags: new Dictionary<int, double> { [openWorld] = 1, [storyRich] = 1 }),
            new PlayedGameWithGenres(Played("2"), [], Tags: new Dictionary<int, double> { [openWorld] = 1, [storyRich] = 0.5 })
        ], DateTimeOffset.UtcNow);
        var game = new CatalogGame("9X", "Some Game", null, null, ["Action & adventure"],
            OnPc: true, OnConsole: true, LeavingSoon: false);
        var names = new Dictionary<int, string> { [openWorld] = "Monde ouvert", [storyRich] = "Scénario riche" };

        var row = new RecommendationRow(new Recommendation(game, 1, [], [openWorld, storyRich]), profile, names);

        Assert.Equal("Parce que tu vas loin dans : Monde ouvert (2 jeux), Scénario riche (2 jeux)", row.ReasonLabel);
    }

    private static PlayedGame Played(string id) =>
        new("Steam", id, "Game " + id, true, 10, 10, 100, DateTimeOffset.UtcNow, PlayStatus.Mastered);

    [Fact]
    public void Genre_match_keeps_the_genres_and_modes_as_reason()
    {
        var game = new CatalogGame("9X", "Some Game", null, null, ["Shooter"],
            OnPc: true, OnConsole: true, LeavingSoon: false);

        var row = Row(game);

        Assert.StartsWith("Même style : Tir", row.ReasonLabel);
    }

    [Fact]
    public void Game_Pass_game_also_on_Steam_keeps_its_Store_rating_and_resized_picture()
    {
        var game = new CatalogGame("9ABC", "Space Marine 2", null, null, ["Shooter"],
            OnPc: true, OnConsole: true, LeavingSoon: false, AverageRating: 4.6, RatingCount: 21_400,
            ImageUrl: "https://store-images.s-microsoft.com/image/apps.1", SteamAppId: "2183900");

        var row = Row(game);

        Assert.True(row.InGamePass);
        Assert.True(row.IsOnSteam);
        Assert.Equal("4,6", row.RatingValueLabel.Replace('.', ','));
        Assert.StartsWith("PC et console", row.PlatformsLabel);
        Assert.Equal("https://store-images.s-microsoft.com/image/apps.1?w=560&h=315&q=80", row.ImageUrl);
    }

    [Theory]
    // In the Game Pass and on Steam: both badges, each its own page.
    [InlineData("9ABC", true, "2183900", "https://www.xbox.com/fr-FR/games/store/-/9ABC", "https://store.steampowered.com/app/2183900/?l=french")]
    // In the Game Pass only: the Xbox store.
    [InlineData("9ABC", true, null, "https://www.xbox.com/fr-FR/games/store/-/9ABC", null)]
    // Sold on Steam only (or in a higher Game Pass plan than the player's): Steam.
    [InlineData("steam:397540", false, "397540", null, "https://store.steampowered.com/app/397540/?l=french")]
    public void Each_platform_badge_opens_the_game_page_there(
        string productId, bool inGamePass, string? steamAppId, string? xboxPage, string? steamPage)
    {
        var game = new CatalogGame(productId, "Un jeu", null, null, ["Shooter"],
            OnPc: inGamePass, OnConsole: inGamePass, LeavingSoon: false, SteamAppId: steamAppId);

        var row = Row(game);

        Assert.Equal(xboxPage is null ? null : new Uri(xboxPage), row.XboxStorePage);
        Assert.Equal(steamPage is null ? null : new Uri(steamPage), row.SteamPage);
    }
}
