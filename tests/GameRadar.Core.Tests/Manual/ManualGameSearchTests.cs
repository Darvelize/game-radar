using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Steam;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Manual;

public class ManualGameSearchTests
{
    [Fact]
    public void Steam_suggestions_keep_the_games_only()
    {
        // Shape of the real answer (IStoreQueryService/SearchSuggestions): type 4 = DLC.
        const string json = """
            { "response": { "store_items": [
                { "success": 1, "appid": 1245620, "name": "ELDEN RING", "type": 0 },
                { "success": 1, "appid": 2778580, "name": "ELDEN RING Shadow of the Erdtree", "type": 4 },
                { "success": 1, "appid": 2622380, "name": "ELDEN RING NIGHTREIGN", "type": 0 }
            ] } }
            """;

        var games = SteamTagClient.ParseGames(json);

        Assert.Equal(["ELDEN RING", "ELDEN RING NIGHTREIGN"], games.Select(g => g.Name));
        Assert.Equal("1245620", games[0].AppId);
    }

    [Fact]
    public void The_catalog_is_searched_ignoring_case_accents_and_editions()
    {
        var catalog = new[]
        {
            Catalog("1", title: "Pokémon™ Legends"),
            Catalog("2", title: "New Pokémon Snap"),
            Catalog("3", title: "Forza Horizon 5"),
            Catalog("4", title: "Pokémon™ Legends Deluxe Edition")
        };

        var found = ManualGameSearch.InCatalog(catalog, "pokemon");

        // Starting with the words typed first; the deluxe edition is the same game.
        Assert.Equal(["Pokémon Legends", "New Pokémon Snap"], found.Select(m => m.Title));
        Assert.All(found, m => Assert.True(m.InGamePass));
    }

    [Fact]
    public void The_most_relevant_steam_game_keeps_its_line()
    {
        // Steam's order is relevance: the 2016 DOOM first, then the 1993 one,
        // same title. The second must not take the line's app id.
        var merged = ManualGameSearch.Merge([], [new SteamSuggestion("379720", "DOOM"), new SteamSuggestion("2280", "DOOM")]);

        Assert.Equal("379720", Assert.Single(merged).SteamAppId);
    }

    [Fact]
    public void A_catalog_game_outside_the_players_plan_is_not_offered_as_game_pass()
    {
        // Ultimate only, seen by a PC Game Pass subscriber.
        var ultimateOnly = Catalog("1", title: "Far Cry 6") with { ConsoleTiers = GamePassTier.Ultimate, PcTiers = GamePassTier.Ultimate };

        var found = Assert.Single(ManualGameSearch.InCatalog([ultimateOnly.ForPlan(GamePassTier.PcGamePass)], "far cry"));

        Assert.False(found.InGamePass);
    }

    [Fact]
    public void Two_titles_without_letters_are_two_games()
    {
        var first = new ManualGame("???", ManualPlatform.Other, ManualLevel.Completed);
        var second = new ManualGame("#", ManualPlatform.Other, ManualLevel.Completed);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void A_game_on_the_game_pass_and_on_steam_is_one_line_with_its_app_id()
    {
        var fromCatalog = ManualGameSearch.InCatalog([Catalog("1", title: "Hades")], "hades");

        var merged = ManualGameSearch.Merge(fromCatalog, [new SteamSuggestion("1145350", "Hades II"), new SteamSuggestion("1145360", "Hades")]);

        Assert.Equal(["Hades", "Hades II"], merged.Select(m => m.Title));
        var hades = merged[0];
        Assert.True(hades.InGamePass && hades.OnSteam);
        Assert.Equal("1145360", hades.SteamAppId);
        Assert.False(merged[1].InGamePass);
    }
}
