using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GameRadar.App.ViewModels;
using GameRadar.Core.Cache;
using GameRadar.Core.Epic;
using GameRadar.Core.GOG;
using GameRadar.Core.GamePass;
using GameRadar.Core.Manual;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;

namespace GameRadar.App.Tests;

/// <summary>
/// The recommendations computed for real: cached catalogs, a game added by
/// hand as the history, and a fake network that can hold a request (the
/// minutes of a first tag lookup, of the Game Pass download).
/// </summary>
public sealed class RecommendationComputationTests : AppTest
{
    // Finished on Switch, found on Steam: a favorite, its tags looked up by app id.
    private static readonly ManualGame Hero = new("Hero Quest", ManualPlatform.Switch, ManualLevel.Completed, SteamAppId: "100");

    private static readonly CatalogGame HeroInGamePass = Game("GP-HERO", "Hero Quest", inGamePass: true);
    private static readonly CatalogGame PassRpg = Game("GP-RPG", "Rpg Pass", inGamePass: true);
    private static readonly CatalogGame SteamRpg = Game("steam:200", "Rpg Steam", inGamePass: false) with { SteamAppId = "200" };

    private static CatalogGame Game(string id, string title, bool inGamePass) =>
        new(id, title, null, null, ["Role playing"], OnPc: inGamePass, OnConsole: inGamePass, LeavingSoon: false);

    /// <summary>The history (one game added by hand) and the cached catalogs, each of the given age (null: none).</summary>
    private async Task PrepareAsync(TimeSpan? gamePassAge, TimeSpan? steamAge, Action<AppSettings>? withSettings = null)
    {
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        settings.ManualGames.Add(Hero);
        withSettings?.Invoke(settings);
        await store.SaveAsync(settings);

        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        if (gamePassAge is { } gp)
            await cache.SaveAsync(new GamePassCatalogSource(cache).CacheKey, TestJson.Serialize(new GameCatalog([HeroInGamePass, PassRpg], DateTimeOffset.UtcNow - gp)));
        if (steamAge is { } steam)
            await cache.SaveAsync(SteamCatalogSource.CacheKey, TestJson.Serialize(new GameCatalog([SteamRpg], DateTimeOffset.UtcNow - steam)));
    }

    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);

    private static IEnumerable<string> Cards(MainWindowViewModel vm) =>
        vm.Recommendations.Sections.SelectMany(s => s.Games).Select(g => g.Title);

    // A 1 x 1 PNG: the smallest picture the cards can decode.
    private static readonly byte[] Picture = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");


    /// <summary>The former French Game Pass catalog, still on disk (the move to English titles did not end).</summary>
    private async Task SeedFrenchCatalogAsync()
    {
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await cache.SaveAsync(EnglishTitlesMigration.FrenchGamePassKey, TestJson.Serialize(
            new GameCatalog([PassRpg with { Title = "RPG Passe" }], DateTimeOffset.UtcNow.AddDays(-20))));
    }

    [AvaloniaFact]
    public async Task A_filter_click_during_the_tag_lookup_stays_once_the_lookup_ends()
    {
        await PrepareAsync(Fresh, Fresh);
        var network = new FakeStores { HoldTags = true };
        var vm = SampleData.Empty(TempFolder, network: network);

        var starting = vm.InitializeAsync();
        // The tag lookup started: the first recommendations are on screen.
        await network.TagsReached.Task;
        Assert.Contains("Rpg Steam", Cards(vm));
        vm.SourceFilter.ShowSteam = false;
        network.Release();
        await starting;

        // Recomputed with the tags found, and still without the Steam games.
        Assert.Equal(["Rpg Pass"], Cards(vm));
    }

    [AvaloniaFact]
    public async Task A_refusal_during_the_tag_lookup_stays_once_the_lookup_ends()
    {
        await PrepareAsync(Fresh, Fresh);
        var network = new FakeStores { HoldTags = true };
        var vm = SampleData.Empty(TempFolder, network: network);

        var starting = vm.InitializeAsync();
        await network.TagsReached.Task;
        var card = vm.Recommendations.Sections.SelectMany(s => s.Games).Single(g => g.Title == "Rpg Steam");
        vm.NotInterested.RefuseCommand.Execute(card);
        network.Release();
        await starting;

        Assert.DoesNotContain("Rpg Steam", Cards(vm));
    }

    [AvaloniaFact]
    public async Task Removing_the_last_game_takes_its_recommendations_away()
    {
        await PrepareAsync(Fresh, Fresh);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());
        await vm.InitializeAsync();
        Assert.NotEmpty(Cards(vm));

        vm.History.Games.Single().Details.Single().Manual!.RemoveCommand.Execute(null);
        await vm.WhenSettingsSavedAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(Cards(vm));
        // A filter click does not bring them back.
        vm.SourceFilter.ShowSteam = false;
        Assert.Empty(Cards(vm));
    }

    [AvaloniaFact]
    public async Task A_game_added_with_genres_found_nowhere_else_counts_in_the_taste()
    {
        // A PlayStation exclusive: no Steam page, not in the Game Pass, nor in the Microsoft Store.
        // Its genres, kept when it was added, are all the taste has to go on.
        await PrepareAsync(Fresh, null, settings =>
        {
            settings.ManualGames.Clear();
            settings.ManualGames.Add(new ManualGame("Exclusive Quest", ManualPlatform.PlayStation, ManualLevel.Completed, Genres: ["Role playing"]));
        });
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        var card = vm.Recommendations.Sections.SelectMany(s => s.Games).Single(g => g.Title == "Rpg Pass");
        Assert.Contains(GenreLabels.Display("Role playing"), card.ReasonLabel);
    }

    [AvaloniaFact]
    public async Task The_same_game_without_genres_says_nothing_of_the_taste()
    {
        await PrepareAsync(Fresh, null, settings =>
        {
            settings.ManualGames.Clear();
            settings.ManualGames.Add(new ManualGame("Exclusive Quest", ManualPlatform.PlayStation, ManualLevel.Completed));
        });
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        var card = vm.Recommendations.Sections.SelectMany(s => s.Games).Single(g => g.Title == "Rpg Pass");
        Assert.DoesNotContain(GenreLabels.Display("Role playing"), card.ReasonLabel);
    }

    [AvaloniaFact]
    public async Task The_Steam_store_recommends_even_without_the_Game_Pass_catalog()
    {
        // First start: the Game Pass download fails, the Steam catalog is there.
        await PrepareAsync(gamePassAge: null, steamAge: Fresh);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        Assert.Contains("Rpg Steam", Cards(vm));
    }

    [AvaloniaFact]
    public async Task Old_catalogs_recommend_right_away_while_the_new_ones_download()
    {
        await PrepareAsync(gamePassAge: TimeSpan.FromDays(10), steamAge: Fresh);
        var network = new FakeStores { HoldGamePass = true };
        var vm = SampleData.Empty(TempFolder, network: network);

        var starting = vm.InitializeAsync();
        await network.GamePassReached.Task;
        // The computation with the cached catalogs ends.
        Assert.True(await BecomesTrueAsync(() => Cards(vm).Any(), 1000));

        Assert.Contains("Rpg Pass", Cards(vm));
        network.Release();
        await starting;
    }

    [AvaloniaFact]
    public async Task Settings_still_in_French_move_to_English_at_the_next_start()
    {
        // The move failed last time (its save): the former French catalog is
        // still there, the English one fresh, no download at this start.
        await PrepareAsync(Fresh, Fresh, settings => settings.NotInterested["rpgpasse"] = "RPG Passe");
        await SeedFrenchCatalogAsync();
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        Assert.DoesNotContain("Rpg Pass", Cards(vm));
        await vm.WhenSettingsSavedAsync();
        Assert.Equal(["Rpg Pass"], new SettingsStore(TempFolder).Load().NotInterested.Values);
    }

    [AvaloniaFact]
    public async Task A_refresh_moves_the_French_titles_and_rebuilds_the_history()
    {
        // A game added by hand under its French title; the former French catalog is still on disk.
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        settings.ManualGames.Add(new ManualGame("RPG Passe", ManualPlatform.Switch, ManualLevel.Completed));
        await store.SaveAsync(settings);
        await SeedFrenchCatalogAsync();
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());
        // The English catalog, as the startup reads it: no download is needed for the move.
        vm.Catalogs.GamePass.Catalog = new GameCatalog([PassRpg], DateTimeOffset.UtcNow);
        Assert.Equal(["RPG Passe"], vm.History.Games.Select(r => r.Name));

        await vm.Catalogs.RefreshAsync([]);

        // Moved in the settings, and the history line shows it at once.
        Assert.Equal(["Rpg Pass"], vm.History.Games.Select(r => r.Name));
        await vm.WhenSettingsSavedAsync();
        Assert.Equal(["Rpg Pass"], new SettingsStore(TempFolder).Load().ManualGames.Select(g => g.Title));
    }

    /// <summary>The usual caches, but the Game Pass catalog gives the card "Rpg Pass" a picture.</summary>
    private async Task PrepareWithPictureAsync()
    {
        await PrepareAsync(Fresh, Fresh);
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await cache.SaveAsync(new GamePassCatalogSource(cache).CacheKey, TestJson.Serialize(
            new GameCatalog([HeroInGamePass, PassRpg with { ImageUrl = "https://images.example/rpg.png" }], DateTimeOffset.UtcNow - Fresh)));
    }

    /// <summary>Starts the app and waits until the picture of "Rpg Pass" is being downloaded (held by the returned fake).</summary>
    private async Task<(MainWindowViewModel Vm, HoldingNetwork Pictures, Task Starting)> StartWithHeldPictureAsync()
    {
        await PrepareWithPictureAsync();
        var pictures = new HoldingNetwork("images.example", Picture);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores(), imageNetwork: pictures);
        var starting = vm.InitializeAsync();
        try
        {
            await pictures.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        catch
        {
            // Nothing stays held and running in the folder the test deletes afterwards.
            pictures.Release();
            throw;
        }
        return (vm, pictures, starting);
    }

    [AvaloniaFact]
    public async Task The_picture_of_a_card_loads_once_downloaded()
    {
        var (vm, pictures, starting) = await StartWithHeldPictureAsync();

        pictures.Release();
        await starting;

        var card = vm.Recommendations.Sections.SelectMany(s => s.Games).Single(g => g.Title == "Rpg Pass");
        Assert.True(await BecomesTrueAsync(() => card.Image is not null, 3000));
    }

    [AvaloniaFact]
    public async Task Stopping_the_background_work_keeps_the_pictures_from_loading()
    {
        // What erasing everything does before it deletes the files: the pictures
        // still waiting are given up, and a finished one is not put on its card.
        // (The file of a download already started is guarded by the image cache itself.)
        var (vm, pictures, starting) = await StartWithHeldPictureAsync();

        vm.Recommendations.Stop();
        pictures.Release();
        await starting;

        var cards = vm.Recommendations.Sections.SelectMany(s => s.Games).ToList();
        // Long enough for a picture nothing stopped to be downloaded, decoded and shown.
        Assert.False(await BecomesTrueAsync(() => cards.Any(c => c.Image is not null), 500));
    }

    [AvaloniaFact]
    public async Task A_cached_GOG_catalog_is_read_at_startup_and_not_downloaded_again()
    {
        await PrepareAsync(Fresh, Fresh);
        var gogRpg = Game("gog:300", "Rpg Gog", inGamePass: false) with { GogStoreLink = "https://www.gog.com/en/game/rpg_gog" };
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await cache.SaveAsync(GogCatalogSource.CacheKey, TestJson.Serialize(new GameCatalog([gogRpg], DateTimeOffset.UtcNow - Fresh)));
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        Assert.Equal("Rpg Gog", Assert.Single(vm.Library.GogCatalog!.Games).Title);
        // Fresh: kept as it is (a download would have failed on the fake network, 404).
        Assert.False(vm.Catalogs.IsRefreshing);
        Assert.StartsWith("1 ", vm.Catalogs.Gog.Status);
    }

    [AvaloniaFact]
    public async Task The_GOG_box_shows_or_hides_the_games_sold_on_GOG()
    {
        await PrepareAsync(Fresh, Fresh);
        var gogRpg = Game("gog:300", "Rpg Gog", inGamePass: false) with { GogStoreLink = "https://www.gog.com/en/game/rpg_gog" };
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await cache.SaveAsync(GogCatalogSource.CacheKey, TestJson.Serialize(new GameCatalog([gogRpg], DateTimeOffset.UtcNow - Fresh)));
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();
        Assert.True(vm.Recommendations.HasGogCandidates);
        Assert.Contains("Rpg Gog", Cards(vm));

        vm.SourceFilter.ShowGog = false;

        Assert.DoesNotContain("Rpg Gog", Cards(vm));
        Assert.Contains("Rpg Steam", Cards(vm));
    }

    [AvaloniaFact]
    public async Task A_history_row_shows_the_tags_found_by_the_computation_without_being_rebuilt()
    {
        await PrepareAsync(Fresh, Fresh);
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await cache.SaveAsync("steam-tag-names-fr", """{"122":"RPG"}""");
        var network = new FakeStores { HoldTags = true };
        var vm = SampleData.Empty(TempFolder, network: network);

        var starting = vm.InitializeAsync();
        await network.TagsReached.Task;
        // Not looked up yet: nothing under the name.
        var row = vm.History.AllRows.Single(r => r.Name == "Hero Quest");
        Assert.Empty(row.TagBadges);
        row.IsExpanded = true;

        network.Release();
        await starting;

        // The same row, still unfolded, with its tag (app 100: "RPG" in the fake store).
        Assert.Same(row, vm.History.AllRows.Single(r => r.Name == "Hero Quest"));
        Assert.True(row.IsExpanded);
        Assert.Equal(["#RPG"], Badges(row));
    }

    [AvaloniaFact]
    public async Task The_tags_filter_finds_the_games_that_count_without_any_tag()
    {
        await PrepareAsync(Fresh, Fresh, settings =>
        {
            // Unknown to Steam (the fake store finds no title): no tags.
            settings.ManualGames.Add(new ManualGame("Mystery Game", ManualPlatform.Switch, ManualLevel.Completed));
            // Unknown to Steam too, but the player gave it one.
            settings.ManualGames.Add(new ManualGame("Lost Game", ManualPlatform.Switch, ManualLevel.Completed));
            settings.AddedTags[GameTitle.Key("Lost Game")] = [19];
            // Left out of the taste by the player: without tags, but they change nothing.
            var shortGame = new ManualGame("Short Game", ManualPlatform.Switch, ManualLevel.PlayedALittle);
            settings.ManualGames.Add(shortGame);
            settings.ExcludedFromTaste.Add(ManualGames.ToPlayed(shortGame).Key);
        });
        await new CacheStore(Path.Combine(TempFolder, "cache")).SaveAsync("steam-tag-names-fr", """{"122":"RPG","19":"Action"}""");
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());
        var tags = vm.History.Filters.Single(g => g.Title == "Tags :");
        // Unticked before the tags are known: nothing can be "Sans tags" yet...
        tags.Choices.Single(c => c.Label == "Avec tags").IsChecked = false;
        Assert.Empty(vm.History.Games);

        await vm.InitializeAsync();

        // ...refiltered when they arrive.
        Assert.Equal(["Mystery Game"], vm.History.Games.Select(r => r.Name));
        Assert.Equal(["#Action"], Badges(vm.History.AllRows.Single(r => r.Name == "Lost Game")));

        tags.Choices.Single(c => c.Label == "Avec tags").IsChecked = true;
        tags.Choices.Single(c => c.Label == "Sans tags").IsChecked = false;

        Assert.Equal(["Hero Quest", "Lost Game", "Short Game"], vm.History.Games.Select(r => r.Name).Order());
    }

    [AvaloniaFact]
    public async Task A_tag_added_with_the_plus_is_saved_and_shown_first_as_a_badge_with_its_cross()
    {
        await PrepareAsync(Fresh, Fresh, settings =>
            settings.ManualGames.Add(new ManualGame("Mystery Game", ManualPlatform.Switch, ManualLevel.Completed)));
        // 113 = Free to Play: not offered, the computation ignores it.
        await new CacheStore(Path.Combine(TempFolder, "cache")).SaveAsync("steam-tag-names-fr", """{"122":"RPG","19":"Action","113":"Free-to-play"}""");
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());
        await vm.InitializeAsync();
        var row = vm.History.AllRows.Single(r => r.Name == "Mystery Game");
        var editor = row.TagEditor;
        // No tag at all: only "+ Tag".
        Assert.True(row.LacksTags);
        Assert.Empty(row.TagBadges);
        Assert.Equal("+ Tag", row.AddTagLabel);
        Assert.True(editor.CanAdd);
        Assert.Equal(["Action", "RPG"], editor.Suggestions);

        // Picked in the list (it fills the box), then "Ajouter".
        editor.Text = "Action";
        editor.AddCommand.Execute(null);

        Assert.Equal(["#Action"], Badges(row));
        Assert.True(row.TagBadges[0].IsAdded);
        Assert.Equal("+", row.AddTagLabel);
        // Not offered twice.
        Assert.Equal(["RPG"], editor.Suggestions);
        Assert.False(row.LacksTags);
        Assert.Equal([19], (await SavedSettingsAsync(vm)).AddedTags[GameTitle.Key("Mystery Game")]);

        // Typed, case ignored, then Enter: after the first one.
        editor.Text = "rpg";
        editor.AddCommand.Execute(null);
        Assert.Equal(["#Action", "#RPG"], Badges(row));
        Assert.Equal("", editor.Text);

        // Removed with its ×.
        row.TagBadges[0].Remove!.Execute(null);

        Assert.Equal(["#RPG"], Badges(row));
        Assert.Equal([122], (await SavedSettingsAsync(vm)).AddedTags[GameTitle.Key("Mystery Game")]);
        // The same row.
        Assert.Same(row, vm.History.AllRows.Single(r => r.Name == "Mystery Game"));
    }

    [AvaloniaFact]
    public async Task The_added_tags_come_before_Steam_ones_which_have_no_cross()
    {
        await PrepareAsync(Fresh, Fresh, settings => settings.AddedTags[GameTitle.Key("Hero Quest")] = [19]);
        await new CacheStore(Path.Combine(TempFolder, "cache")).SaveAsync("steam-tag-names-fr", """{"122":"RPG","19":"Action"}""");
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());
        await vm.InitializeAsync();

        // Hero Quest: RPG on Steam (fake store), Action added by the player.
        var row = vm.History.AllRows.Single(r => r.Name == "Hero Quest");

        Assert.Equal(["#Action", "#RPG"], Badges(row));
        Assert.Equal([true, false], row.TagBadges.Select(b => b.IsAdded));
        Assert.Null(row.TagBadges[1].Remove);
    }

    [AvaloniaFact]
    public async Task An_added_tag_Steam_no_longer_names_is_shown_by_its_number_and_can_be_removed()
    {
        await PrepareAsync(Fresh, Fresh, settings =>
        {
            settings.ManualGames.Add(new ManualGame("Mystery Game", ManualPlatform.Switch, ManualLevel.Completed));
            settings.AddedTags[GameTitle.Key("Mystery Game")] = [999, 19];
        });
        await new CacheStore(Path.Combine(TempFolder, "cache")).SaveAsync("steam-tag-names-fr", """{"19":"Action"}""");
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());
        await vm.InitializeAsync();
        var row = vm.History.AllRows.Single(r => r.Name == "Mystery Game");

        Assert.Equal(["#Tag n° 999", "#Action"], Badges(row));
        row.TagBadges[0].Remove!.Execute(null);

        Assert.Equal([19], (await SavedSettingsAsync(vm)).AddedTags[GameTitle.Key("Mystery Game")]);
    }

    [AvaloniaFact]
    public async Task A_row_whose_last_added_tag_is_removed_stays_while_filtered()
    {
        await PrepareAsync(Fresh, Fresh, settings =>
        {
            // Unknown to Steam, one tag added by the player.
            settings.ManualGames.Add(new ManualGame("Lost Game", ManualPlatform.Switch, ManualLevel.Completed));
            settings.AddedTags[GameTitle.Key("Lost Game")] = [19];
        });
        await new CacheStore(Path.Combine(TempFolder, "cache")).SaveAsync("steam-tag-names-fr", """{"122":"RPG","19":"Action"}""");
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());
        await vm.InitializeAsync();
        vm.History.Filters.Single(g => g.Title == "Tags :").Choices.Single(c => c.Label == "Sans tags").IsChecked = false;
        var row = vm.History.Games.Single(r => r.Name == "Lost Game");

        // Its last ×: now "Sans tags", but it does not vanish under the mouse.
        row.TagBadges[0].Remove!.Execute(null);

        Assert.True(row.LacksTags);
        Assert.Contains(row, vm.History.Games);
    }

    private static string[] Badges(GameGroupRow row) => row.TagBadges.Select(b => b.Label).ToArray();

    [AvaloniaFact]
    public async Task Before_the_tag_names_are_known_no_tag_can_be_added()
    {
        await PrepareAsync(Fresh, Fresh);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        var row = vm.History.AllRows.Single(r => r.Name == "Hero Quest");

        Assert.False(row.TagEditor.CanAdd);
        Assert.Empty(row.TagEditor.Suggestions);
    }

    [AvaloniaFact]
    public async Task A_cached_PlayStation_Store_catalog_is_read_and_described_on_its_line()
    {
        await PrepareAsync(Fresh, Fresh);
        var psGame = Game("psstore:EP-RPG", "Rpg PlayStation", inGamePass: false) with { PlayStationProductId = "EP-RPG" };
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await cache.SaveAsync(PlayStationStoreCatalogSource.CacheKey, TestJson.Serialize(new GameCatalog([psGame], DateTimeOffset.UtcNow - Fresh)));
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        Assert.Equal("EP-RPG", Assert.Single(vm.Catalogs.PsStore.Catalog!.Games).PlayStationProductId);
        Assert.StartsWith("1 ", vm.Catalogs.PsStore.Status);
    }

    [AvaloniaFact]
    public async Task The_Epic_box_shows_or_hides_the_games_sold_on_Epic()
    {
        await PrepareAsync(Fresh, Fresh);
        var epicRpg = Game("epic:ns-rpg", "Rpg Epic", inGamePass: false) with { EpicStoreLink = "https://store.epicgames.com/fr/p/rpg-epic" };
        var cache = new CacheStore(Path.Combine(TempFolder, "cache"));
        await cache.SaveAsync(EpicCatalogSource.CacheKey, TestJson.Serialize(new GameCatalog([epicRpg], DateTimeOffset.UtcNow - Fresh)));
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();
        Assert.True(vm.Recommendations.HasEpicCandidates);
        Assert.Contains("Rpg Epic", Cards(vm));
        Assert.StartsWith("1 ", vm.Catalogs.Epic.Status);

        vm.SourceFilter.ShowEpic = false;

        Assert.DoesNotContain("Rpg Epic", Cards(vm));
        Assert.Contains("Rpg Steam", Cards(vm));
        Assert.False((await SavedSettingsAsync(vm)).ShowEpic);
    }

    [AvaloniaFact]
    public async Task A_missing_GOG_catalog_is_downloaded_at_startup_and_its_failure_shown()
    {
        await PrepareAsync(Fresh, Fresh);
        var vm = SampleData.Empty(TempFolder, network: new FakeStores());

        await vm.InitializeAsync();

        // The fake network answers 404: the line says the catalog is not answering.
        Assert.Null(vm.Library.GogCatalog);
        Assert.Equal(GameRadar.Core.Resources.CoreStrings.Gog_CatalogDown, vm.Catalogs.Gog.Status);
        // The other sources still recommend.
        Assert.NotEmpty(Cards(vm));
    }
}
