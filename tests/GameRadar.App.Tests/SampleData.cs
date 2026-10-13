using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GameRadar.App.Resources;
using GameRadar.App.ViewModels;
using GameRadar.Core.Cache;
using GameRadar.Core.GamePass;
using GameRadar.Core.Logging;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Epic;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;
using GameRadar.Core.Xbox;

namespace GameRadar.App.Tests;

/// <summary>
/// The base of the App test classes: a data folder of their own, deleted
/// once the background settings saves are done (<see cref="SampleData.CleanUpAsync"/>).
/// </summary>
public abstract class AppTest : IAsyncDisposable
{
    protected string TempFolder { get; }

    protected AppTest() =>
        TempFolder = Path.Combine(Path.GetTempPath(), $"gameradar-{GetType().Name}-{Guid.NewGuid():N}");

    public async ValueTask DisposeAsync() => await SampleData.CleanUpAsync(TempFolder);

    /// <summary>Waits, pumping the UI thread, until the condition holds (false after the time limit).</summary>
    protected static async Task<bool> BecomesTrueAsync(Func<bool> condition, int milliseconds)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (condition())
                return true;
            await Task.Delay(10);
        }
        return condition();
    }

    /// <summary>
    /// The settings as saved, once the view model's background saves are done:
    /// they queue on its lock as soon as they start, so waiting for it is exact
    /// (polling the file had a time limit a busy test run sometimes overran).
    /// </summary>
    protected async Task<AppSettings> SavedSettingsAsync(MainWindowViewModel vm)
    {
        await vm.WhenSettingsSavedAsync();
        return new SettingsStore(TempFolder).Load();
    }
}

/// <summary>No network: every request answers 404 (Sony, the catalogs, Steam...).</summary>
internal sealed class OfflineNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
}

/// <summary>
/// A view model filled with hand-written sample data, as the screens look
/// once everything is loaded. Public catalog titles only: never a real
/// play history. No network: InitializeAsync is never called.
/// </summary>
internal static class SampleData
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Now;

    /// <summary>First launch: no key, no history.</summary>
    /// <param name="network">A fake server for the tests that make requests; none by default.</param>
    /// <param name="imageNetwork">A fake server for the pictures of the cards; the real network by default (no card has a picture then).</param>
    public static MainWindowViewModel Empty(string tempFolder, Action<Uri>? openBrowser = null, Action? quit = null, HttpMessageHandler? network = null, HttpMessageHandler? imageNetwork = null)
    {
        var secrets = new SecretStore(tempFolder);
        var settings = new SettingsStore(tempFolder);
        var cache = new CacheStore(Path.Combine(tempFolder, "cache"));
        var images = new ImageFileCache(imageNetwork is null ? new HttpClient() : new HttpClient(imageNetwork), Path.Combine(tempFolder, "images"));
        var log = new ErrorLog(Path.Combine(tempFolder, "logs"), "test");
        var vm = new MainWindowViewModel(
            network is null ? new HttpClient() : new HttpClient(network),
            settings,
            cache,
            images,
            new XboxAccount(network is null ? new HttpClient() : new HttpClient(network), secrets),
            new SteamAccount(secrets),
            new PlayStationAccount(secrets),
            new EpicAccount(network is null ? new HttpClient() : new HttpClient(network), secrets),
            openBrowser ?? (_ => { }),
            new LocalDataEraser(settings, cache, secrets, images, log),
            quit ?? (() => { }),
            log,
            restart: () => { },
            dataFolder: new DataFolder(tempFolder));
        Built.GetOrAdd(tempFolder, _ => new()).Add(vm);
        return vm;
    }

    // Every view model built on a folder: its background work (settings saves, often
    // started with "_ = ", lookups, pictures) is awaited before the folder is deleted.
    // Deleting while one wrote settings.json.tmp made a test fail now and then.
    private static readonly ConcurrentDictionary<string, ConcurrentBag<MainWindowViewModel>> Built = new();

    // Long enough for any save or cancelled lookup; a guard in case the UI thread,
    // where they end, were not running during the cleanup.
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(10);

    /// <summary>The folder of GameRadar.sln, above the test's output folder.</summary>
    public static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "GameRadar.sln")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException("GameRadar.sln not found above the test folder.");
    }

    /// <summary>
    /// The cleanup of a test: stops the background work of its view models and waits until
    /// nothing writes anymore (settings saves, a cancelled lookup saving what it found,
    /// card pictures), then deletes its folder.
    /// </summary>
    public static async Task CleanUpAsync(string tempFolder)
    {
        if (Built.TryRemove(tempFolder, out var viewModels))
        {
            var idle = Task.WhenAll(viewModels.Select(vm => vm.WhenIdleAsync()));
            await Task.WhenAny(idle, Task.Delay(IdleWait));
        }
        if (Directory.Exists(tempFolder))
            Directory.Delete(tempFolder, recursive: true);
    }

    public static MainWindowViewModel ViewModel(string tempFolder)
    {
        // An Extra subscriber with the PlayStation Store box ticked, saved before the window reads
        // its settings: choosing them on a built window would download the catalogs (no network here).
        var store = new SettingsStore(tempFolder);
        var settings = store.Load();
        settings.PsPlusPlan = PsPlusTier.Extra;
        settings.ShowPlayStationStore = true;
        store.SaveAsync(settings).GetAwaiter().GetResult();

        var vm = Empty(tempFolder);
        vm.History.HasHistory = true;
        vm.Accounts.XboxSync.IsConnected = true;
        vm.Accounts.AccountLabel = "Connecté en tant que RadarPlayer";
        vm.Accounts.SteamSync.IsConnected = true;
        vm.Accounts.SteamAccountLabel = "Connecté en tant que Radar Player";
        vm.Accounts.XboxSync.SyncLabel = "il y a 2 h";
        vm.Accounts.SteamSync.SyncLabel = "il y a 2 h";
        vm.Accounts.SteamSync.ShowSync = true;
        vm.CatalogSyncLabel = "Catalogue : hier";

        vm.Accounts.XboxSync.Status = "180 jeux, dont 4 platinés — historique du 24/09/2026 à 09:12.";
        vm.Accounts.SteamSync.Status = "197 jeux possédés, dont 156 lancés et 3 à 100 % — historique du 24/09/2026 à 09:12.";
        vm.History.Summary = "308 jeux, dont 7 platinés, 2 terminés.";
        vm.Catalogs.GamePass.Status = "903 jeux dans ton Game Pass Ultimate, dont 68 de tes jeux — catalogue du 23/09/2026 à 18:40.";
        vm.Catalogs.PsPlus.Status = "465 jeux (Extra et Premium) — catalogue du 23/09/2026 à 18:40.";
        vm.Catalogs.Steam.Status = "1 375 jeux bien notés parmi les meilleures ventes — catalogue du 23/09/2026 à 18:40.";
        vm.Catalogs.Gog.Status = "886 jeux bien notés parmi les meilleures ventes — catalogue du 23/09/2026 à 18:40.";
        vm.Catalogs.Epic.Status = "214 jeux parmi les meilleures ventes, les plus joués et les mieux notés — catalogue du 23/09/2026 à 18:40.";
        vm.Catalogs.PsStore.Status = "558 jeux parmi les meilleures ventes PS5 et PS4 — catalogue du 23/09/2026 à 18:40.";
        vm.Recommendations.HasSteamCandidates = true;
        vm.Recommendations.HasGogCandidates = true;
        vm.Recommendations.HasEpicCandidates = true;
        vm.Recommendations.Status = "6 de tes jeux favoris servent de point de départ, sur 103 jeux analysés dans ton historique. Jeux proposés : Game Pass Ultimate, PlayStation Plus Extra, catalogue Steam, catalogue GOG, catalogue Epic et catalogue PlayStation Store.";

        var destiny = Played("Destiny 2", 75);
        vm.Recommendations.Sections.Add(new RecommendationSectionRow(
            "À jouer avant qu'ils quittent le Game Pass",
            [
                new RecommendationRow(new LeavingSoonPick(Similar(Game("Remnant II", ["Shooter", "Action & adventure", "Role playing"], 4.5, 12840, PlayModes.Solo | PlayModes.CoopOnline, leaving: true) with { Tags = Tags(29482, 3814, 3843) }, "Shooter", "Role playing"), destiny), TagNames),
                new RecommendationRow(new LeavingSoonPick(Similar(Game("Like a Dragon: Infinite Wealth", ["Role playing", "Action & adventure"], 4.7, 3950, PlayModes.Solo, leaving: true), "Role playing"), Played("Control", 73)))
            ],
            isLeavingSoon: true));
        vm.Recommendations.Sections.Add(new RecommendationSectionRow(
            "Parce que tu as aimé Destiny 2",
            [
                new RecommendationRow(Similar(Game("Warhammer 40,000: Space Marine 2", ["Shooter", "Action & adventure"], 4.6, 21400, PlayModes.Solo | PlayModes.CoopOnline | PlayModes.MultiOnline, steamAppId: "2183900") with { Tags = Tags(19, 3814, 3843) }, "Shooter", "Action & adventure"), TagNames),
                // Compared on Steam tags: the reason names the shared tags.
                new RecommendationRow(new SimilarGame(new Recommendation(Game("Tom Clancy's The Division 2", ["Shooter", "Role playing"], 4.4, 88100, PlayModes.Solo | PlayModes.CoopOnline) with { Tags = Tags(353880, 3814, 3843) }, 0.5, []), 0.67, ["Shooter"], PlayModes.CoopOnline, ByTags: true, SharedTags: [353880, 3814, 3843]), TagNames),
                new RecommendationRow(Similar(Game("Borderlands 3", ["Shooter", "Role playing", "Action & adventure"], 4.4, 187_500, PlayModes.Solo | PlayModes.CoopOnline | PlayModes.Local, steamAppId: "397540", inGamePass: false), "Shooter"))
            ]));

        // The PS Plus catalog: a game only there, and one also sold on Steam and GOG; a game
        // only sold on Epic, one only on the PlayStation Store, one only sold on GOG (public titles).
        vm.Recommendations.Sections.Add(new RecommendationSectionRow(
            "D'après tes goûts",
            [
                new RecommendationRow(Similar(PsPlusGame("Horizon Zero Dawn", ["Action & adventure", "Role playing"], rating: null, count: 0, conceptId: "10000263"), "Action & adventure", "Role playing")),
                new RecommendationRow(Similar(PsPlusGame("Dead Cells", ["Action & adventure"], rating: 4.7, count: 120_000, conceptId: "10001029", steamAppId: "588650") with { GogStoreLink = "https://www.gog.com/en/game/dead_cells" }, "Action & adventure")),
                new RecommendationRow(Similar(EpicGame("alan-wake-2", "Alan Wake 2", ["Action & adventure"], 4.8), "Action & adventure")),
                new RecommendationRow(Similar(PsStoreGame("EP9000-CUSA00900_00-BLOODBORNE0000EU", "Bloodborne", ["Action & adventure", "Role playing"], 4.8), "Role playing")),
                new RecommendationRow(Similar(GogGame("disco_elysium", "Disco Elysium - The Final Cut",["Role playing"], 4.6, 13_200), "Role playing"))
            ]));

        // Generated pictures (no network). The last card keeps none, to show
        // the placeholder of a picture still loading or unavailable.
        var cards = vm.Recommendations.Sections.SelectMany(s => s.Games).ToList();
        (uint, uint)[] palettes = [(0xFF2B1D5C, 0xFFE0703A), (0xFF0F3D3E, 0xFF7BC8A4), (0xFF3A0F2E, 0xFFF2B134), (0xFF10233F, 0xFF4FA3E0)];
        for (int i = 0; i < cards.Count - 1; i++)
            cards[i].Image = Picture(palettes[i % palettes.Length]);

        vm.History.Games.Add(Row(destiny, inGamePass: true, tags: "Tir avec butin · Coop en ligne · FPS"));
        // A game on both platforms, unfolded: one detail line per platform.
        var warframe = Group(
            [Played("Warframe", 24), new PlayedGame("Steam", "230410", "Warframe", true, 212, 552, 38, Now.AddDays(-3), PlayStatus.Played, PlaytimeMinutes: 310 * 60)],
            tags: "Action · Coop en ligne · Tir à la 3ᵉ personne");
        warframe.IsExpanded = true;
        vm.History.Games.Add(warframe);
        vm.History.Games.Add(Row(Played("RoboCop: Rogue City", 100, PlayStatus.Mastered), inGamePass: true));
        vm.History.Games.Add(Row(Played("Hellblade: Senua's Sacrifice", 100, PlayStatus.Mastered)));
        // Story finished: solid flag.
        vm.History.Games.Add(Row(Played("Control", 73), inGamePass: true, completed: true, tags: "Surnaturel · Action · Scénario riche"));
        // Barely played: left out of the taste by default (outlined heart).
        vm.History.Games.Add(Row(Played("Starfield", 2)));
        vm.History.Games.Add(Row(Played("Cyberpunk 2077", 62) with { Platform = "Steam", PlaytimeMinutes = 95 * 60 }, tags: "Cyberpunk · Monde ouvert · RPG"));
        vm.History.Games.Add(Row(new PlayedGame("Steam", "413150", "Stardew Valley", false, 0, null, 0, Now.AddDays(-40), PlayStatus.Played, PlaytimeMinutes: 42 * 60)));
        // PlayStation: platinum earned, the DLC trophies keep the progress at 62 %: finished.
        vm.History.Games.Add(Row(new PlayedGame("PlayStation", "CUSA11993_00", "Marvel's Spider-Man", true, 51, 62, 62, Now.AddDays(-60), PlayStatus.Completed, PlaytimeMinutes: 42 * 60)));
        // GOG: only an achievement percentage, no counts.
        vm.History.Games.Add(Row(new PlayedGame("GOG", "1207664663", "The Witcher 3: Wild Hunt", true, 0, null, 49, Now.AddDays(-75), PlayStatus.Played, PlaytimeMinutes: 110 * 60)));
        // EA app (EA Play): every achievement, its play time.
        vm.History.Games.Add(Row(new PlayedGame("EA", "star-wars-jedi-fallen-order", "STAR WARS Jedi: Fallen Order™", true, 39, 39, 100, Now.AddDays(-90), PlayStatus.Mastered, PlaytimeMinutes: 64 * 60)));
        // Epic Games: its play time, no achievements and no last-played date (Epic gives none).
        vm.History.Games.Add(Row(new PlayedGame("Epic", "Sugar", "Rocket League", false, 0, null, 0, null, PlayStatus.Played, PlaytimeMinutes: 25 * 60)));
        // Added by hand (played on Switch), unfolded: its editing line.
        var hollowKnight = new ManualGame("Hollow Knight", ManualPlatform.Switch, ManualLevel.Completed, SteamAppId: "367520");
        var manualRow = new GameGroupRow([(ManualGames.ToPlayed(hollowKnight), null)],
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), Now, onUserChoiceChanged: _ => { },
            manualEditorFor: _ => new ManualGameEditor(hollowKnight, _ => { }, _ => { }),
            // Two tags added by the player: shown first under the name, as pills when unfolded.
            tagEditor: new GameTagEditor([1628, 29482], _ => { })) { TagBadges = [Added("Metroidvania"), Added("Soulslike"), FromSteam("Action")] };
        manualRow.TagEditor.SetChoices(Choices);
        manualRow.IsExpanded = true;
        vm.History.Games.Insert(1, manualRow);
        vm.History.FilterStatus = "11 jeux affichés sur 308";
        // Rows added by hand above: the platform boxes are offered as in the real list.
        foreach (var box in vm.History.PlatformFilter.Choices)
            box.IsOffered = true;

        return vm;
    }

    // French names of a few Steam tags, as GetTagList?language=french gives them.
    private static readonly IReadOnlyDictionary<int, string> TagNames = new Dictionary<int, string>
    {
        [353880] = "Tir avec butin",
        [3814] = "Tir à la 3ᵉ personne",
        [3843] = "Coop en ligne",
        [29482] = "Soulslike",
        [19] = "Action",
        [1628] = "Metroidvania"
    };

    // Badges as ShowTags builds them: a tag the player added has its ×, Steam's have none.
    private static TagBadge Added(string name) => new(Localization.Format(Strings.Tag_Badge, name), IsAdded: true, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));

    private static TagBadge FromSteam(string name) => new(Localization.Format(Strings.Tag_Badge, name), IsAdded: false, Remove: null);

    // The tags offered to the player, as the history builds them from the names.
    private static readonly SteamTagChoices Choices = new(TagNames);

    /// <summary>Steam tags, most voted first (weights as SteamTagIndex gives them).</summary>
    private static Dictionary<int, double> Tags(params int[] ids) =>
        ids.Select((id, i) => (id, Weight: 1.0 - i * 0.1)).ToDictionary(t => t.id, t => t.Weight);

    private static PlayedGame Played(string name, double progress, PlayStatus status = PlayStatus.Played) =>
        new("Xbox", name.GetHashCode().ToString(), name, true, (int)(progress / 2), null, progress, Now.AddDays(-12), status);

    private static CatalogGame Game(string title, string[] genres, double rating, int count, PlayModes modes, bool leaving = false, string? steamAppId = null, bool inGamePass = true) =>
        new(title, title, null, null, genres, OnPc: inGamePass, OnConsole: inGamePass, LeavingSoon: leaving,
            AverageRating: rating, RatingCount: count, Modes: modes, SteamAppId: steamAppId);

    private static CatalogGame PsPlusGame(string title, string[] genres, double? rating, int count, string conceptId, string? steamAppId = null) =>
        new($"psplus:{conceptId}", title, null, null, genres, OnPc: false, OnConsole: false, LeavingSoon: false,
            AverageRating: rating, RatingCount: count, SteamAppId: steamAppId,
            PsPlusTiers: PsPlusTier.Extra | PsPlusTier.Premium, PlayStationConceptId: conceptId);

    private static CatalogGame GogGame(string slug, string title, string[] genres, double rating, int count) =>
        new($"gog:{slug}", title, null, null, genres, OnPc: false, OnConsole: false, LeavingSoon: false,
            AverageRating: rating, RatingCount: count, Modes: PlayModes.Solo, GogStoreLink: $"https://www.gog.com/en/game/{slug}");

    private static CatalogGame EpicGame(string slug, string title, string[] genres, double rating) =>
        new($"epic:{slug}", title, null, null, genres, OnPc: false, OnConsole: false, LeavingSoon: false,
            AverageRating: rating, Modes: PlayModes.Solo, EpicStoreLink: $"https://store.epicgames.com/p/{slug}");

    private static CatalogGame PsStoreGame(string productId, string title, string[] genres, double rating) =>
        new($"psstore:{productId}", title, null, null, genres, OnPc: false, OnConsole: false, LeavingSoon: false,
            AverageRating: rating, PlayStationProductId: productId);

    private static SimilarGame Similar(CatalogGame game, params string[] sharedGenres) =>
        new(new Recommendation(game, 0.5, sharedGenres), 0.9, sharedGenres, game.Modes);

    /// <summary>A 16:9 picture: diagonal gradient between two ARGB colors.</summary>
    private static Bitmap Picture((uint From, uint To) colors)
    {
        const int width = 560, height = 315;
        var pixels = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double t = (x + y) / (double)(width + height);
                pixels[y * width + x] = (int)Blend(colors.From, colors.To, t);
            }

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        return bitmap;
    }

    private static uint Blend(uint a, uint b, double t)
    {
        uint Channel(int shift) => (uint)(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t) << shift;
        return 0xFF000000 | Channel(16) | Channel(8) | Channel(0);
    }

    private static GameGroupRow Row(PlayedGame game, bool inGamePass = false, bool completed = false, string tags = "") =>
        Group([game], inGamePass, completed, tags);

    /// <param name="completed">Marked "Histoire finie" by the player.</param>
    /// <param name="tags">What ShowTags would set: its 3 most voted Steam tags.</param>
    private static GameGroupRow Group(PlayedGame[] games, bool inGamePass = false, bool completed = false, string tags = "")
    {
        var row = new GameGroupRow(games.Select(g => (g, inGamePass ? Game(g.Name, ["Shooter"], 4.5, 1000, PlayModes.Solo) : null)).ToList(),
            new HashSet<string>(), new HashSet<string>(),
            completed ? games.Select(g => g.Key).ToHashSet() : new HashSet<string>(),
            Now, onUserChoiceChanged: _ => { }) { TagBadges = [.. tags.Split(" · ", StringSplitOptions.RemoveEmptyEntries).Select(FromSteam)] };
        // The tag names are known: every row has its "+".
        row.TagEditor.SetChoices(Choices);
        return row;
    }
}
