using GameRadar.Core.Amazon;
using GameRadar.Core.Cache;
using GameRadar.Core.Epic;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Steam;

namespace GameRadar.App.ViewModels;

/// <summary>
/// The "Jeux gratuits" page, for information only (nothing to do with "Pour toi"):
/// the games Epic, Steam, PS Plus and Amazon Prime give away now. Nothing is asked before
/// the page is opened; then each source whose list is older than a day, or has an
/// offer that ended, is downloaded (PS Plus only for a player who has a plan).
/// </summary>
public sealed class FreeGamesViewModel
{
    private readonly ImageFileCache _imageCache;
    private readonly NoticeCenter _notices;
    private readonly BackgroundWork _work;
    private bool _cachesRead;

    public FreeGamesViewModel(HttpClient http, CacheStore cache, ImageFileCache imageCache, NoticeCenter notices, SourceFilterViewModel filter, BackgroundWork work)
    {
        _imageCache = imageCache;
        _notices = notices;
        _work = work;
        Sections =
        [
            Section("Epic Games", "Epic free games", new FreeGameCache(cache, FreeGameCache.EpicKey),
                now => new EpicFreeGamesClient(http).FetchAsync(now),
                game => UntilLabel(Strings.FreeGames_Until, game),
                link => CardPicture.StorePage(link, Localization.EpicStoreLocale, "store.epicgames.com"), CardPicture.EpicSize),
            // Games given away, and free weekends (to play, not to keep). Steam's pictures have no resizing parameters.
            Section("Steam", "Steam free games", new FreeGameCache(cache, FreeGameCache.SteamKey),
                now => new SteamFreeGamesClient(http).FetchAsync(now),
                game => UntilLabel(game.PlayOnly ? Strings.FreeGames_SteamWeekend : Strings.FreeGames_Until, game),
                link => CardPicture.StorePage(link, locale: null, "store.steampowered.com") is { } page ? Localization.SteamPage(page) : null,
                imageSize: null),
            Section("PlayStation Plus", "PS Plus monthly games", new FreeGameCache(cache, FreeGameCache.PsPlusKey),
                _ => new PsPlusCatalogClient(http).FetchMonthlyGamesAsync(),
                _ => Strings.FreeGames_PsPlusMonthly,
                link => CardPicture.StorePage(link, Localization.PlayStationStoreLocale, "store.playstation.com"), CardPicture.ServerSize,
                isWanted: () => filter.HasAnyPsPlus),
            // Amazon's original pictures are already small JPEGs; its claim pages have no language.
            Section("Amazon Prime", "Amazon free games", new FreeGameCache(cache, FreeGameCache.AmazonKey),
                now => new AmazonFreeGamesClient(http).FetchAsync(now),
                game => UntilLabel(Strings.FreeGames_PrimeUntil, game),
                link => CardPicture.StorePage(link, locale: null, "gaming.amazon.com", "luna.amazon.com"), imageSize: null),
        ];
    }

    /// <param name="page">The store page of a link, in the app's language; null for a link outside the store's sites (the links come from a cache file).</param>
    /// <param name="imageSize">The image server's resizing parameters, or null (pictures as they are).</param>
    /// <param name="isWanted">Null: always asked.</param>
    private static FreeGameSection Section(
        string brand, string logContext, FreeGameCache cache, Func<DateTimeOffset, Task<IReadOnlyList<FreeGame>>> fetch,
        Func<FreeGame, string> offerLabel, Func<string, Uri?> page, string? imageSize, Func<bool>? isWanted = null) => new()
    {
        Title = brand,
        LogContext = logContext,
        FetchingStatus = Strings.FreeGames_Fetching,
        ErrorMessage = ex => NoticeCenter.ServiceErrorMessage(ex,
            Localization.Format(Strings.FreeGames_Unreachable, brand), Localization.Format(Strings.FreeGames_Slow, brand)),
        Cache = cache,
        Fetch = fetch,
        IsWanted = isWanted ?? (() => true),
        ToRow = game => page(game.Link) is { } storePage
            ? new FreeGameRow(game.Title, offerLabel(game),
                game.RedeemOn is { } store ? Localization.Format(Strings.FreeGames_RedeemOn, store) : null, storePage,
                game.ImageUrl is { } url && imageSize is not null ? CardPicture.Resized(url, imageSize) : game.ImageUrl)
            : null,
    };

    /// <summary>Epic, Steam, PS Plus, Amazon Prime: one block each on the page.</summary>
    public IReadOnlyList<FreeGameSection> Sections { get; }

    /// <summary>The page is shown: the cached lists at once, then the downloads it needs. Kept until it ends (tests).</summary>
    public Task OpenAsync() => _work.Track(ShowAndRefreshAsync());

    // No ConfigureAwait(false): the sections are updated on the UI thread. Started
    // without being awaited (page change): an unexpected error is reported here.
    private async Task ShowAndRefreshAsync()
    {
        try
        {
            if (!_cachesRead)
            {
                // File reads: off the UI thread.
                var lists = await Task.Run(() => Sections.Select(s => s.Cache.Load()).ToList());
                _cachesRead = true;
                for (int i = 0; i < Sections.Count; i++)
                    Sections[i].List ??= lists[i];
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var section in Sections)
                section.Show(now);
            var pictures = LoadPicturesAsync();

            await Task.WhenAll(Sections.Where(s => s.NeedsDownload(now)).Select(async section =>
            {
                if (await section.FetchAsync(_notices, now) != FetchResult.Done)
                    return;
                section.Show(DateTimeOffset.UtcNow);
                await LoadPicturesAsync();
            }));
            await pictures;
        }
        catch (Exception ex)
        {
            _notices.ReportUnexpected("Free games", ex);
        }
    }

    // A card is never replaced as a batch (each one stops its own load when it goes away).
    private Task LoadPicturesAsync() =>
        CardPicture.LoadAllAsync(_imageCache, Sections.SelectMany(s => s.Games), CancellationToken.None);

    private static string UntilLabel(string format, FreeGame game) =>
        game.Until is { } until ? Localization.Format(format, until.ToLocalTime()) : string.Empty;
}
