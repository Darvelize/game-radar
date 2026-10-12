using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Steam;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Manual;

/// <summary>"Ajouter un jeu" also searches the PlayStation Store, and the PS Plus catalog already loaded.</summary>
public class PlayStationSearchTests
{
    private static PlayStationStoreGame Store(string title, string id = "EP9000-PPSA1_00-0000") => new(id, title);

    private static CatalogGame PsPlus(string title, params string[] genres) =>
        Catalog("psplus:" + title.Length, title: title, genres: genres) with { PsPlusTiers = PsPlusTier.Extra | PsPlusTier.Premium };

    // ---------- The PS Plus catalog ----------

    [Fact]
    public void The_ps_plus_catalog_is_searched_like_the_game_pass_one()
    {
        var catalog = new[]
        {
            PsPlus("Bloodborne", "Action & adventure", "Role playing"),
            PsPlus("Bloodborne Digital Deluxe Edition", "Action & adventure"),
            PsPlus("Astro Bot", "Platformer")
        };

        var found = ManualGameSearch.InPsPlusCatalog(catalog, "blood");

        // The two editions are one game; it is a PlayStation game, not a Game Pass one.
        var line = Assert.Single(found);
        Assert.Equal("Bloodborne", line.Title);
        Assert.True(line.OnPlayStation);
        Assert.False(line.InGamePass);
        Assert.Equal(["Action & adventure", "Role playing"], line.Genres);
    }

    [Fact]
    public void An_empty_text_finds_nothing_in_the_ps_plus_catalog()
    {
        Assert.Empty(ManualGameSearch.InPsPlusCatalog([PsPlus("Astro Bot")], "  "));
    }

    // ---------- Merge ----------

    [Fact]
    public void A_playstation_exclusive_is_a_line_of_its_own_with_its_product_id()
    {
        var merged = ManualGameSearch.Merge([], [], fromPlayStationStore: [Store("Astro Bot™", "EP9000-PPSA21567_00-0000")]);

        var line = Assert.Single(merged);
        Assert.Equal("Astro Bot", line.Title);
        Assert.True(line.OnPlayStation);
        Assert.Equal("EP9000-PPSA21567_00-0000", line.PlayStationProductId);
        Assert.Null(line.SteamAppId);
        Assert.False(line.InGamePass);
    }

    [Fact]
    public void Editions_and_console_versions_of_one_game_are_one_line()
    {
        var merged = ManualGameSearch.Merge([], [], fromPlayStationStore:
            [Store("God of War™", "EP1"), Store("God of War™ Digital Deluxe Edition", "EP2"), Store("God of War", "EP3")]);

        var line = Assert.Single(merged);
        // The first, the most relevant, gives the product id.
        Assert.Equal("EP1", line.PlayStationProductId);
    }

    [Fact]
    public void A_game_on_steam_and_on_the_playstation_store_is_one_line_with_both()
    {
        var line = Assert.Single(ManualGameSearch.Merge([], [new SteamSuggestion("1245620", "Elden Ring")],
            fromPlayStationStore: [Store("ELDEN RING", "EP0")]));

        Assert.Equal("1245620", line.SteamAppId);
        Assert.True(line.OnSteam);
        Assert.True(line.OnPlayStation);
        Assert.Equal("EP0", line.PlayStationProductId);
    }

    [Fact]
    public void A_game_in_ps_plus_and_in_the_store_is_one_line_with_the_genres_and_the_product_id()
    {
        var fromPsPlus = ManualGameSearch.InPsPlusCatalog([PsPlus("Bloodborne", "Action & adventure")], "bloodborne");

        var line = Assert.Single(ManualGameSearch.Merge([], [], fromPsPlus: fromPsPlus, fromPlayStationStore: [Store("Bloodborne™", "EP7")]));

        Assert.Equal("EP7", line.PlayStationProductId);
        Assert.Equal(["Action & adventure"], line.Genres);
    }

    [Fact]
    public void The_sources_are_combined_in_a_stable_order()
    {
        var fromCatalog = ManualGameSearch.InCatalog([Catalog("1", title: "Halo Infinite")], "halo");

        var merged = ManualGameSearch.Merge(fromCatalog, [new SteamSuggestion("1", "Halo Wars")],
            fromXboxStore: [new("9N1", "Halo 3")], fromPlayStationStore: [Store("Halo Dummy")]);

        // Game Pass, Steam, Xbox Store, then PlayStation.
        Assert.Equal(["Halo Infinite", "Halo Wars", "Halo 3", "Halo Dummy"], merged.Select(m => m.Title));
    }

    [Fact]
    public void Without_the_playstation_sources_the_merge_is_as_before()
    {
        var line = Assert.Single(ManualGameSearch.Merge([], [new SteamSuggestion("379720", "DOOM")]));

        Assert.False(line.OnPlayStation);
        Assert.Null(line.PlayStationProductId);
        Assert.Null(line.Genres);
    }
}
