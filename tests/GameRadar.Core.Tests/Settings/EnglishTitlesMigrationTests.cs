using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Settings;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Settings;

/// <summary>
/// The settings saved with French titles (1.2.0 and before) move once to the
/// English titles, through the former French catalogs (same product ids).
/// </summary>
public sealed class EnglishTitlesMigrationTests : IDisposable
{
    private const string French = "Dishonored® : La mort de l'Outsider™";
    private const string English = "Dishonored®: Death of the Outsider™";

    private static readonly IReadOnlyList<CatalogGame> FrenchGames = [Catalog("P1", French), Catalog("P2", "Halo Infinite")];
    private static readonly IReadOnlyList<CatalogGame> EnglishGames = [Catalog("P1", English), Catalog("P2", "Halo Infinite")];

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void A_refusal_moves_to_the_english_title()
    {
        var settings = new AppSettings();
        settings.NotInterested[GameTitle.Key(French)] = French;
        settings.NotInterested[GameTitle.Key("Halo Infinite")] = "Halo Infinite";

        bool changed = EnglishTitlesMigration.Apply(settings, FormerOf(FrenchGames, EnglishGames));

        Assert.True(changed);
        Assert.Equal(English, settings.NotInterested[GameTitle.Key(English)]);
        Assert.False(settings.NotInterested.ContainsKey(GameTitle.Key(French)));
        // Same title in both languages: untouched.
        Assert.Equal("Halo Infinite", settings.NotInterested[GameTitle.Key("Halo Infinite")]);
    }

    [Fact]
    public void A_game_added_by_hand_keeps_its_choices_under_its_new_title_id()
    {
        var settings = new AppSettings();
        var game = new ManualGame(French, ManualPlatform.OtherPcStore, ManualLevel.PlayedALittle);
        settings.ManualGames.Add(game);
        string oldKey = ManualGames.ToPlayed(game).Key;
        settings.MarkedCompleted.Add(oldKey);
        settings.IncludedInTaste.Add(oldKey);

        EnglishTitlesMigration.Apply(settings, FormerOf(FrenchGames, EnglishGames));

        var renamed = Assert.Single(settings.ManualGames);
        // Cleaned like the titles "Ajouter un jeu" saves (no ™ nor ®).
        Assert.Equal(GameTitle.Clean(English), renamed.Title);
        // Without a Steam app id, its id is its title: the choices follow it.
        string newKey = ManualGames.ToPlayed(renamed).Key;
        Assert.NotEqual(oldKey, newKey);
        Assert.Equal([newKey], settings.MarkedCompleted);
        Assert.Equal([newKey], settings.IncludedInTaste);
    }

    [Fact]
    public void A_game_added_by_hand_with_a_steam_app_id_keeps_its_id()
    {
        var settings = new AppSettings();
        settings.ManualGames.Add(new ManualGame(French, ManualPlatform.OtherPcStore, ManualLevel.Completed, SteamAppId: "614570"));

        EnglishTitlesMigration.Apply(settings, FormerOf(FrenchGames, EnglishGames));

        var renamed = Assert.Single(settings.ManualGames);
        // Cleaned like the titles "Ajouter un jeu" saves (no ™ nor ®).
        Assert.Equal(GameTitle.Clean(English), renamed.Title);
        Assert.Equal("614570", renamed.Id);
    }

    [Fact]
    public void Nothing_to_move_changes_nothing()
    {
        var settings = new AppSettings();
        settings.NotInterested[GameTitle.Key("Halo Infinite")] = "Halo Infinite";
        settings.ManualGames.Add(new ManualGame("Zelda", ManualPlatform.Switch, ManualLevel.Mastered));

        Assert.False(EnglishTitlesMigration.Apply(settings, FormerOf(FrenchGames, EnglishGames)));
    }

    [Fact]
    public async Task The_former_french_catalog_waits_for_the_english_one_and_for_the_saved_settings()
    {
        var cache = await FrenchGamePassCacheAsync(FrenchGames);
        var settings = new AppSettings();
        settings.NotInterested[GameTitle.Key(French)] = French;

        // Offline at the first start: no English catalog yet, nothing moves.
        Assert.False(EnglishTitlesMigration.Apply(settings, Former(cache, null)));

        Assert.True(EnglishTitlesMigration.Apply(settings, Former(cache, new GameCatalog(EnglishGames, Now))));
        Assert.True(settings.NotInterested.ContainsKey(GameTitle.Key(English)));
        // Translated in memory only: kept until the settings are saved (a crash
        // or a failed save before that, and the move runs again).
        Assert.NotNull(cache.TryLoad(EnglishTitlesMigration.FrenchGamePassKey));

        await EnglishTitlesMigration.TrimFormerCatalogsAsync(settings, cache, Former(cache, new GameCatalog(EnglishGames, Now)));

        // Done: deleted, so the move never runs again.
        Assert.Null(cache.TryLoad(EnglishTitlesMigration.FrenchGamePassKey));
        Assert.False(EnglishTitlesMigration.Apply(settings, Former(cache, new GameCatalog(EnglishGames, Now))));
    }

    [Fact]
    public async Task A_refused_game_out_of_todays_catalog_is_translated_the_day_it_comes_back()
    {
        var cache = await FrenchGamePassCacheAsync(FrenchGames);
        var settings = new AppSettings();
        settings.NotInterested[GameTitle.Key(French)] = French;
        // P1 (the refused game) has left the catalog for now.
        var withoutIt = new GameCatalog([Catalog("P2", "Halo Infinite")], Now);

        Assert.False(EnglishTitlesMigration.Apply(settings, Former(cache, withoutIt)));
        await EnglishTitlesMigration.TrimFormerCatalogsAsync(settings, cache, Former(cache, withoutIt));

        // Kept, only with the game the refusal still waits for: not read
        // whole at every start for months.
        Assert.Equal(["P1"], cache.LoadJson(EnglishTitlesMigration.FrenchGamePassKey, TestJsonContext.Default.GameCatalog)!.Games.Select(g => g.ProductId));

        // Back in the catalog: translated, then the French catalog goes.
        var withIt = new GameCatalog(EnglishGames, Now);
        Assert.True(EnglishTitlesMigration.Apply(settings, Former(cache, withIt)));
        Assert.True(settings.NotInterested.ContainsKey(GameTitle.Key(English)));
        await EnglishTitlesMigration.TrimFormerCatalogsAsync(settings, cache, Former(cache, withIt));
        Assert.Null(cache.TryLoad(EnglishTitlesMigration.FrenchGamePassKey));
    }

    [Fact]
    public async Task A_setting_is_translated_once_even_when_both_catalogs_chain_titles()
    {
        // The Game Pass translates A into B, and the Steam catalog has a game
        // whose French title is B: one table, applied once, stops at B.
        var cache = await FrenchGamePassCacheAsync([Catalog("P1", "Titre A")]);
        await cache.SaveAsync(EnglishTitlesMigration.FrenchSteamKey, TestJson.Serialize(new GameCatalog([Catalog("steam:1", "Title B")], Now)));
        var settings = new AppSettings();
        settings.NotInterested[GameTitle.Key("Titre A")] = "Titre A";

        EnglishTitlesMigration.Apply(settings, EnglishTitlesMigration.ReadFormerCatalogs(cache,
            new GameCatalog([Catalog("P1", "Title B")], Now),
            new GameCatalog([Catalog("steam:1", "Title C")], Now)));

        Assert.Equal("Title B", Assert.Single(settings.NotInterested).Value);
    }

    // A former catalog and its English one, without the disk.
    private static IReadOnlyList<EnglishTitlesMigration.FormerCatalog> FormerOf(IReadOnlyList<CatalogGame> french, IReadOnlyList<CatalogGame> english) =>
        [new(EnglishTitlesMigration.FrenchGamePassKey, french, english)];

    // The former Game Pass catalog on disk with this English one (the Steam one not loaded).
    private static IReadOnlyList<EnglishTitlesMigration.FormerCatalog> Former(CacheStore cache, GameCatalog? englishGamePass) =>
        EnglishTitlesMigration.ReadFormerCatalogs(cache, englishGamePass, englishSteam: null);

    private async Task<CacheStore> FrenchGamePassCacheAsync(IReadOnlyList<CatalogGame> games)
    {
        var cache = new CacheStore(_folder.Path);
        await cache.SaveAsync(EnglishTitlesMigration.FrenchGamePassKey, TestJson.Serialize(new GameCatalog(games, Now)));
        return cache;
    }
}
