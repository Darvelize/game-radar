using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GameRadar.Core.Epic;
using GameRadar.Core.Models;
using GameRadar.Core.Resources;
using GameRadar.Core.Services;

namespace GameRadar.Core.PlayStation;

/// <summary>A game found by the PlayStation Store search: a product id (to ask its genres) and its title.</summary>
public sealed record PlayStationStoreGame(string ProductId, string Title);

/// <summary>A best seller of the store: its product (page, details), its title and its card picture.</summary>
internal sealed record StoreBestSeller(string ProductId, string Title, string? ImageUrl);

/// <summary>What a product page tells: genres in the engine's vocabulary, the players' rating out of 5.</summary>
internal sealed record StoreDetails(IReadOnlyList<string> Genres, double? Rating);

/// <summary>
/// The PlayStation Store: the search of "Ajouter un jeu", for the games found
/// neither in the Game Pass nor on Steam (PlayStation exclusives), and the best
/// sellers of the store catalog. No account: the NPSSO and the tokens are never
/// sent here, only what the player typed.
/// </summary>
/// <remarks>
/// The site's GraphQL accepts only the persisted queries of its own pages, by
/// their hash (a query sent as text answers 400 "Query not whitelisted"). The
/// hashes belong to the version of the site: when Sony updates it they are
/// refused, which this client reports as a <see cref="PlayStationException"/>
/// (the page then falls back on the PS Plus catalog). A new hash is read from the
/// requests of store.playstation.com in the browser's developer tools.
/// </remarks>
public sealed class PlayStationStoreClient
{
    private const string Endpoint = "https://web.np.playstation.com/api/graphql/v1/op";

    // Hashes of the site's persisted queries, as of its version 0.114.0 (oct. 2026).
    private const string SearchHash = "4df6284f982e57bec70f23c77e2c219dc792eb19af7fb3d3a81767aa3f1958aa";
    private const string GenresHash = "71375f8f3dba0de83520ecd474069e037f8ea23b4efcb6778aef58894cd4452d";
    private const string GridHash = "4ce7d410a4db2c8b635a48c1dcec375906ff63b19dadd87e073f8fd0c0481d35";

    // "All PS5 games" and "All PS4 games" of the European store (reportingName WM_EU_…).
    internal const string Ps5Games = "4cbf39e2-5749-4970-ba81-93a489e4570c";
    internal const string Ps4Games = "44d8bb20-653e-431e-8ad0-c0a365f68d2f";

    private const string SiteVersion = "0.114.0";

    // en-GB, not the player's country: the French store ("en-FR") answers no result
    // at all, and the British one gives English titles, like every other source.
    private const string Locale = "en-GB";

    private const int PageSize = 24;

    // Best sellers of the last 30 days per console: 300 + 300 give ~590 distinct
    // games (PS4 and PS5 versions share a title), measured on 12/10/2026.
    internal const int BestSellersPerConsole = 300;

    // Product pages asked at once (~1.5 KB each; 80 in 3.5 s at 8, no rate limit, measured).
    internal const int MaxDetailsAtOnce = 8;

    // The bar of the Epic store, the other rating out of 5 (user's choice, 12/10/2026).
    public const double MinRating = EpicCatalogClient.MinRating;

    // Sony gives no number of ratings either: a rated game weighs as much as an Epic
    // one, or the engine's smoothing would bring every rating back to its prior.
    internal const int RatingWeight = EpicCatalogClient.RatingWeight;

    private readonly HttpClient _http;

    public PlayStationStoreClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// The games the Store proposes for what was typed (1 request): the full
    /// games only, not their editions, add-ons, demos or costumes. Empty for an empty text.
    /// </summary>
    /// <exception cref="PlayStationException">The site refused the query or answered something unexpected.</exception>
    public async Task<IReadOnlyList<PlayStationStoreGame>> SearchGamesAsync(string term, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
            return [];

        var variables = new StoreSearchVariables
        {
            CountryCode = "GB",
            LanguageCode = "en",
            NextCursor = "",
            PageOffset = 0,
            PageSize = PageSize,
            SearchTerm = term.Trim()
        };
        return ParseSearch(await GetAsync(
            "getSearchResults", variables, PlayStationJsonContext.Default.StoreSearchVariables, SearchHash, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// The genres of a game found by <see cref="SearchGamesAsync"/> (1 request),
    /// in the vocabulary of the whole engine; empty when the Store lists none.
    /// </summary>
    /// <exception cref="PlayStationException">The site refused the query or answered something unexpected.</exception>
    public async Task<IReadOnlyList<string>> FetchGenresAsync(string productId, CancellationToken ct = default) =>
        (await FetchDetailsAsync(productId, ct).ConfigureAwait(false)).Genres;

    /// <summary>A product page (1 request): its genres and the players' rating.</summary>
    /// <exception cref="PlayStationException">The site refused the query or answered something unexpected.</exception>
    internal async Task<StoreDetails> FetchDetailsAsync(string productId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        return ParseDetails(await GetAsync(
            "queryRetrieveTelemetryDataPDPProduct", new StoreProductVariables { ProductId = productId },
            PlayStationJsonContext.Default.StoreProductVariables, GenresHash, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// The first <see cref="BestSellersPerConsole"/> full games of a store category,
    /// best sellers of the last 30 days first (1 request); with
    /// <paramref name="preOrdersOnly"/>, only those not released yet.
    /// </summary>
    /// <exception cref="PlayStationException">The site refused the query or answered something unexpected.</exception>
    internal async Task<IReadOnlyList<StoreBestSeller>> FetchGridAsync(
        string category, bool preOrdersOnly, CancellationToken ct = default)
    {
        List<string> filters = ["storeDisplayClassification:FULL_GAME"];
        if (preOrdersOnly)
            filters.Add("productReleaseDate:future_release_date");
        var variables = new StoreGridVariables
        {
            Id = category,
            PageArgs = new StorePageArgs { Size = BestSellersPerConsole },
            SortBy = new StoreSort { Name = "sales30", IsAscending = false },
            FilterBy = filters
        };
        return ParseGrid(await GetAsync(
            "categoryGridRetrieve", variables, PlayStationJsonContext.Default.StoreGridVariables, GridHash, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// The best sellers of both consoles, the pre-orders left out, one game
    /// per title (the better seller's product), with their genres and rating
    /// (<see cref="MaxDetailsAtOnce"/> pages at a time), rated 3/5 at least.
    /// </summary>
    /// <exception cref="PlayStationException">The site refused a list, limited the requests, or no page could be read.</exception>
    public async Task<IReadOnlyList<CatalogGame>> FetchCatalogAsync(CancellationToken ct = default)
    {
        // The four lists at once (independent).
        var lists = await Task.WhenAll(
            FetchGridAsync(Ps5Games, preOrdersOnly: false, ct),
            FetchGridAsync(Ps4Games, preOrdersOnly: false, ct),
            FetchGridAsync(Ps5Games, preOrdersOnly: true, ct),
            FetchGridAsync(Ps4Games, preOrdersOnly: true, ct)).ConfigureAwait(false);
        var preOrders = lists[2].Concat(lists[3]).Select(g => g.ProductId).ToHashSet();
        // Rank by rank, PS5 first: the better seller of a title keeps it.
        var unique = Interleave(lists[0], lists[1])
            .Where(g => !preOrders.Contains(g.ProductId))
            .DistinctBy(g => GameTitle.Key(g.Title))
            .ToList();

        // Not one page among well-known games: the details query broke, the old catalog stays.
        var details = await StoreLookup.FetchAllAsync(unique, MaxDetailsAtOnce,
            async (game, token) => await FetchDetailsAsync(game.ProductId, token).ConfigureAwait(false),
            () => new PlayStationException(CoreStrings.PlayStation_StoreRefused), ct).ConfigureAwait(false);

        return unique.Select((game, i) => ToCatalogGame(game, details[i])).OfType<CatalogGame>().ToList();
    }

    private static IEnumerable<StoreBestSeller> Interleave(IReadOnlyList<StoreBestSeller> first, IReadOnlyList<StoreBestSeller> second)
    {
        for (int i = 0; i < Math.Max(first.Count, second.Count); i++)
        {
            if (i < first.Count)
                yield return first[i];
            if (i < second.Count)
                yield return second[i];
        }
    }

    /// <summary>
    /// The best seller as a candidate, or null when rated below <see cref="MinRating"/>
    /// (a game without rating is kept: the best sellers already vouch for it).
    /// </summary>
    internal static CatalogGame? ToCatalogGame(StoreBestSeller game, StoreDetails? details)
    {
        double? rating = details?.Rating;
        if (rating < MinRating)
            return null;

        return new CatalogGame(
            ProductId: "psstore:" + game.ProductId,
            Title: game.Title,
            XboxTitleId: null,
            PackageFamilyName: null,
            Genres: details?.Genres ?? [],
            OnPc: false,
            OnConsole: false,
            LeavingSoon: false,
            AverageRating: rating,
            RatingCount: rating is null ? 0 : RatingWeight,
            ImageUrl: game.ImageUrl,
            PlayStationProductId: game.ProductId);
    }

    internal static IReadOnlyList<StoreBestSeller> ParseGrid(string json)
    {
        var grid = Deserialize(json, PlayStationJsonContext.Default.StoreGridEnvelope)?.Data?.CategoryGridRetrieve
            ?? throw new PlayStationException(CoreStrings.PlayStation_StoreRefused);

        return (grid.Products ?? [])
            .Where(p => p is { StoreDisplayClassification: "FULL_GAME", Id: not null } && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new StoreBestSeller(p.Id!, p.Name!.Trim(), CardPicture(p.Media)))
            .ToList();
    }

    // The wide banner fits the 16:9 card; the master art (square) otherwise.
    private static string? CardPicture(List<StoreMedia>? media)
    {
        var images = (media ?? []).Where(m => m.Type == "IMAGE" && !string.IsNullOrWhiteSpace(m.Url)).ToList();
        return (images.FirstOrDefault(m => m.Role == "SIXTEEN_BY_NINE_BANNER") ?? images.FirstOrDefault(m => m.Role == "MASTER"))?.Url;
    }

    internal static IReadOnlyList<PlayStationStoreGame> ParseSearch(string json)
    {
        var results = Deserialize(json, PlayStationJsonContext.Default.StoreSearchEnvelope)?.Data?.UniversalSearch
            ?? throw new PlayStationException(CoreStrings.PlayStation_StoreRefused);

        return (results.Results ?? [])
            // A concept has no classification: a game without a sellable product, left out.
            .Where(r => r is { StoreDisplayClassification: "FULL_GAME", Id: not null } && !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new PlayStationStoreGame(r.Id!, r.Name!.Trim()))
            .ToList();
    }

    internal static StoreDetails ParseDetails(string json)
    {
        var product = Deserialize(json, PlayStationJsonContext.Default.StoreProductEnvelope)?.Data?.ProductRetrieve
            ?? throw new PlayStationException(CoreStrings.PlayStation_StoreRefused);

        // "Role Playing Games" becomes ROLE_PLAYING_GAMES, as the lists of playstation.com write them.
        var genres = (product.LocalizedGenres ?? [])
            .Select(g => g.Value)
            .OfType<string>()
            .Select(v => PlayStationClient.GenreOf(v.Trim().ToUpperInvariant().Replace(' ', '_')))
            .OfType<string>()
            .Distinct()
            .Take(PlayStationClient.MaxGenres)
            .ToList();
        // 0 is what Sony gives a game nobody rated yet (pre-orders, measured): no rating, not a 0/5.
        return new StoreDetails(genres, product.StarRating?.AverageRating is > 0 and var rating ? rating : null);
    }

    private static T? Deserialize<T>(string json, JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException ex)
        {
            throw new PlayStationException(CoreStrings.PlayStation_Format, inner: ex);
        }
    }

    private async Task<string> GetAsync<T>(
        string operation, T variables, JsonTypeInfo<T> variablesInfo, string hash, CancellationToken ct)
    {
        string extensions = JsonSerializer.Serialize(
            new PersistedQueryExtensions { PersistedQuery = new PersistedQueryDto { Version = 1, Sha256Hash = hash } },
            PlayStationJsonContext.Default.PersistedQueryExtensions);
        string url = $"{Endpoint}?operationName={operation}"
            + $"&variables={Uri.EscapeDataString(JsonSerializer.Serialize(variables, variablesInfo))}"
            + $"&extensions={Uri.EscapeDataString(extensions)}";

        // Headers on the request, not on the shared client; the same as the site's own pages send.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("x-psn-store-locale-override", Locale);
        request.Headers.Add("apollographql-client-name", "@sie-ppr-web-store/app");
        request.Headers.Add("apollographql-client-version", SiteVersion);
        // The site answers 400 to a request without a Content-Type, even a GET: an empty JSON body gives it
        // (the header cannot be added to a request without content).
        request.Content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new PlayStationException(CoreStrings.PlayStation_StoreRefused);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw PlayStationException.RateLimited();
        if (!response.IsSuccessStatusCode)
            throw new PlayStationException(
                CoreText.Format(CoreStrings.PlayStation_HttpError, (int)response.StatusCode, response.ReasonPhrase));

        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
