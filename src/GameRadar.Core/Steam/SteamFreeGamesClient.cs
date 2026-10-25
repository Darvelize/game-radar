using System.Globalization;
using System.Text.Json;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Steam.SteamCatalogClient;

namespace GameRadar.Core.Steam;

/// <summary>
/// What Steam gives away now, for the "Jeux gratuits" page: the games free to
/// keep, and the free weekends (free to play for a while, not to keep). Through
/// the store's public query (IStoreQueryService/Query, no key, no account).
/// </summary>
public sealed class SteamFreeGamesClient
{
    private const string AppPage = "https://store.steampowered.com/app/";

    // 100 % off is ignored by the API (the whole store comes back, measured);
    // 99 % gives exactly the items at 100 %.
    private static readonly PriceFiltersDto GivenAway = new() { MinDiscountPercent = 99 };

    private static readonly DataRequestDto Pictures = new() { IncludeAssets = true };
    private static readonly DataRequestDto NoData = new();

    /// <summary>Pages of 1,000 best sellers read for the free weekends (their own count: not the catalog's).</summary>
    public const int WeekendPages = 2;

    private readonly HttpClient _http;

    public SteamFreeGamesClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// The games given away, then the free weekends, ending first (3 requests at once, then
    /// the weekends' pictures). A game in both keeps its card "to keep". The weekends are read
    /// in the best sellers: Steam has no filter for them.
    /// </summary>
    /// <exception cref="SteamException">The store is down or its answer unreadable.</exception>
    // ponytail: a free weekend of a game outside the 2,000 best sellers is missed; a third page if it matters.
    public async Task<IReadOnlyList<FreeGame>> FetchAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // A whole page: games on sale at 99 % come back too (filtered out after),
        // they must not crowd out a game given away. Usually a handful (measured).
        var givenAway = GetStringAsync(_http, TopSellersUrl(0, Pictures, GivenAway), CoreStrings.Steam_FreeGamesDown, ct);
        // Without pictures: ~0.8 MB a page instead of ~1.8 (measured); those of the few
        // weekends found are asked after, in one small request (~5 KB).
        var pages = TopSellerPagesAsync(_http, NoData, WeekendPages, CoreStrings.Steam_FreeGamesDown, ct);
        // Both awaited before either is read: a failure of one never leaves the other unobserved.
        await Task.WhenAll(givenAway, pages).ConfigureAwait(false);
        var toKeep = ParseFreeToKeep(await givenAway.ConfigureAwait(false), now);
        // A game also given away keeps its card "to keep"; a game moving from one page
        // to the next between the two requests is there once.
        var givenAwayLinks = toKeep.Select(g => g.Link).ToHashSet();
        var weekends = (await pages.ConfigureAwait(false)).SelectMany(page => WeekendItems(page, now))
            .Where(item => !givenAwayLinks.Contains(LinkOf(item))).DistinctBy(i => i.AppId).ToList();
        try
        {
            if (weekends.Count > 0)
                await AddPicturesAsync(weekends, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (StoreLookup.Failed(ex, ct))
        {
            // Only the pictures: the cards stay, without one.
        }

        return toKeep.Concat(weekends.Select(ToWeekendGame)).OrderBy(g => g.Until).ToList();
    }

    // The pictures of these games (IStoreBrowseService/GetItems), put on them.
    private async Task AddPicturesAsync(List<StoreItemDto> games, CancellationToken ct)
    {
        string url = InputUrl(SteamTagClient.GetItemsUrl, new GetItemsRequest
        {
            Ids = games.Select(g => new AppIdDto { AppId = g.AppId!.Value }).ToList(),
            DataRequest = Pictures
        }, SteamJsonContext.Default.GetItemsRequest);
        string json = await GetStringAsync(_http, url, CoreStrings.Steam_FreeGamesDown, ct).ConfigureAwait(false);
        var assets = Items(json).GroupBy(i => i.AppId!.Value).ToDictionary(g => g.Key, g => g.First().Assets);
        foreach (var game in games)
            game.Assets = assets.GetValueOrDefault(game.AppId!.Value);
    }

    /// <summary>The games free to keep at <paramref name="now"/> (one without an end stays, without date).</summary>
    /// <exception cref="SteamException">The answer has no response.</exception>
    internal static IReadOnlyList<FreeGame> ParseFreeToKeep(string json, DateTimeOffset now)
    {
        var games = new List<FreeGame>();
        foreach (var item in Items(json))
        {
            if (item.BestPurchaseOption is not { IsFreeToKeep: true } option)
                continue;
            DateTimeOffset? until = option.FreeToKeepEnds is long end ? FromUnix(end) : null;
            if (until is null || until > now)
                games.Add(ToFreeGame(item, until, playOnly: false));
        }
        return games;
    }

    // The items of a page on a free weekend now (started, not ended); an answer without response throws.
    private static IEnumerable<StoreItemDto> WeekendItems(string json, DateTimeOffset now) =>
        Items(json).Where(item => item.FreeWeekend is { StartTime: long start, EndTime: long end }
            && FromUnix(start) <= now && now < FromUnix(end));

    private static FreeGame ToWeekendGame(StoreItemDto item) => ToFreeGame(item, FromUnix(item.FreeWeekend!.EndTime!.Value), playOnly: true);

    // Known, named and not adult (the app is shared). No "store_items" at all
    // when nothing matches (measured): an empty list, not an error.
    private static IEnumerable<StoreItemDto> Items(string json)
    {
        var response = JsonSerializer.Deserialize(json, SteamJsonContext.Default.QueryEnvelope)?.Response
            ?? throw new SteamException(CoreStrings.Steam_StoreFormat);
        return (response.StoreItems ?? []).Where(item => item is { Success: 1, AppId: not null, Name.Length: > 0 } && !IsAdult(item));
    }

    private static FreeGame ToFreeGame(StoreItemDto item, DateTimeOffset? until, bool playOnly) => new(
        item.Name!,
        LinkOf(item),
        until,
        ImageUrlOf(item.Assets),
        PlayOnly: playOnly);

    private static string LinkOf(StoreItemDto item) => AppPage + item.AppId!.Value.ToString(CultureInfo.InvariantCulture) + "/";

    private static DateTimeOffset FromUnix(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);
}
