using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.GamePass;

/// <summary>
/// Connector for the Game Pass catalog, through two unofficial Microsoft
/// endpoints: the Game Pass lists (product ids only), then the Store
/// catalog for the details (title, genres, ids). No API key needed.
/// </summary>
public sealed class GamePassClient
{
    private const string ListsUrl = "https://catalog.gamepass.com/sigls/v2";
    private const string PlanListsUrl = "https://catalog.gamepass.com/sigls/v3";
    private const string ProductsUrl = "https://displaycatalog.mp.microsoft.com/v7.0/products";
    private const string AutosuggestUrl = "https://displaycatalog.mp.microsoft.com/v7.0/productFamilies/autosuggest";

    private const string LeavingSoonListId = "393f05bf-e596-4ef6-9487-6d4fa0eab987";

    /// <summary>
    /// One Game Pass list per plan and platform ("sigls" v3): list id, plus
    /// the plan's Store product id as subscription context, which filters
    /// the list for that plan. Ids from github.com/nanhopper/catabox (PC Game
    /// Pass: its Store product CFQ7TTC0KGQ8 on the PC list), checked on the
    /// French catalog: Essential ⊂ Premium ⊂ Ultimate on both platforms, and
    /// Ultimate PC = PC Game Pass + 42 Ubisoft+ Classics games.
    /// </summary>
    private static readonly PlanList[] PlanLists =
    [
        new(GamePassTier.Ultimate, Pc: false, "97c6c862-d28a-4907-a3d5-c401f2296a53", "cfq7ttc0khs0"),
        new(GamePassTier.Ultimate, Pc: true, "97c6c862-d28a-4907-a3d5-c401f2296a53", "cfq7ttc0khs0"),
        new(GamePassTier.Premium, Pc: false, "09a72c0d-c466-426a-9580-b78955d8173a", "cfq7ttc0p85b"),
        new(GamePassTier.Premium, Pc: true, "09a72c0d-c466-426a-9580-b78955d8173a", "cfq7ttc0p85b"),
        new(GamePassTier.Essential, Pc: false, "34031711-5a70-4196-bab7-45757dc2294e", "cfq7ttc0k5dj"),
        new(GamePassTier.Essential, Pc: true, "34031711-5a70-4196-bab7-45757dc2294e", "cfq7ttc0k5dj"),
        new(GamePassTier.PcGamePass, Pc: true, "609d944c-d395-4c0a-9ea4-e9f39b52c1ad", "cfq7ttc0kgq8"),
    ];

    private sealed record PlanList(GamePassTier Tier, bool Pc, string ListId, string Subscription);

    // The Store catalog accepts at least 200 ids per request: 5 requests
    // for the whole catalog instead of hundreds.
    private const int BatchSize = 200;

    private readonly HttpClient _http;
    private readonly string _market;
    private readonly string _language;

    /// <param name="http">
    /// Shared HttpClient. It should have automatic gzip decompression
    /// enabled: the full catalog weighs ~11 MB compressed, ~47 MB otherwise.
    /// </param>
    /// <param name="market">Store market, e.g. "FR" (the catalog differs per country).</param>
    /// <param name="language">Language of the titles: English, the language every source agrees on (a game is matched across platforms by its title).</param>
    public GamePassClient(HttpClient http, string market = "FR", string language = "en-us")
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _market = market;
        _language = language;
    }

    /// <summary>
    /// Fetches the whole catalog, every plan and platform: 8 list requests
    /// (7 plan lists + leaving soon), then one request per batch of 200 products.
    /// </summary>
    public async Task<IReadOnlyList<CatalogGame>> FetchCatalogAsync(CancellationToken ct = default)
    {
        // Product id -> plans including it, per platform.
        var consoleTiers = new Dictionary<string, GamePassTier>();
        var pcTiers = new Dictionary<string, GamePassTier>();
        foreach (var list in PlanLists)
        {
            var tiers = list.Pc ? pcTiers : consoleTiers;
            foreach (string id in await FetchPlanListAsync(list, ct).ConfigureAwait(false))
                tiers[id] = tiers.GetValueOrDefault(id) | list.Tier;
        }
        var leavingIds = (await FetchListAsync(LeavingSoonListId, ct).ConfigureAwait(false)).ToHashSet();

        // Many games are in several lists: ask for each product only once.
        var allIds = consoleTiers.Keys.Union(pcTiers.Keys).ToList();
        var pcIds = pcTiers.Keys.ToHashSet();
        var consoleIds = consoleTiers.Keys.ToHashSet();

        var games = new List<CatalogGame>(allIds.Count);
        // Batches are sent one after the other rather than in parallel:
        // a few seconds more, but gentle with an unofficial endpoint.
        foreach (var batch in allIds.Chunk(BatchSize))
        {
            string json = await GetStringAsync(ProductsUrlFor(batch), ct).ConfigureAwait(false);
            games.AddRange(ParseProductsJson(json, pcIds, consoleIds, leavingIds, consoleTiers, pcTiers));
        }

        return games;
    }

    /// <summary>
    /// Finds the Store product of an Xbox game from its title id (1 request).
    /// Returns null when the Store does not know it (typically PC games
    /// from other stores). The Store accepts only one id per lookup.
    /// </summary>
    public async Task<string?> LookupProductIdAsync(string xboxTitleId, CancellationToken ct = default)
    {
        string url = $"{ProductsUrl}/lookup?value={Uri.EscapeDataString(xboxTitleId)}"
            + $"&alternateId=XboxTitleId&market={_market}&languages={_language}";
        string? json = await GetStringOrNullAsync(url, ct, notFoundIsNull: true).ConfigureAwait(false);
        return json is null ? null : ParseProducts(json).FirstOrDefault()?.ProductId;
    }

    /// <summary>
    /// Finds the Store product of a game from its name (1 request), for games
    /// from other platforms (Steam). Only a game with exactly the same title
    /// counts: "Hades" must not become "Hades II". Null when not found.
    /// </summary>
    public async Task<string?> SearchProductIdAsync(string name, CancellationToken ct = default)
    {
        string url = $"{AutosuggestUrl}?query={Uri.EscapeDataString(name)}"
            + $"&market={_market}&languages={_language}&productFamilyNames=Games";
        string? json = await GetStringOrNullAsync(url, ct, notFoundIsNull: true).ConfigureAwait(false);
        return json is null ? null : PickSearchResult(json, name);
    }

    /// <summary>
    /// The games the Store proposes for what was typed (1 request, no key),
    /// every Xbox game and not only the Game Pass ones: how "Ajouter un jeu"
    /// finds an exclusive. Add-ons and bundles are left out. Empty when the
    /// Store knows nothing of it.
    /// </summary>
    public async Task<IReadOnlyList<StoreGameSuggestion>> SuggestGamesAsync(string term, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
            return [];

        string url = $"{AutosuggestUrl}?query={Uri.EscapeDataString(term.Trim())}"
            + $"&market={_market}&languages={_language}&productFamilyNames=Games";
        string? json = await GetStringOrNullAsync(url, ct, notFoundIsNull: true).ConfigureAwait(false);
        return json is null ? [] : ParseSuggestions(json);
    }

    internal static IReadOnlyList<StoreGameSuggestion> ParseSuggestions(string json) =>
        (JsonSerializer.Deserialize(json, GamePassJsonContext.Default.AutosuggestResponseDto)?.Results ?? [])
            .SelectMany(r => r.Products ?? [])
            .Where(p => p is { Type: "Game", ProductId: not null, Title: not null })
            .Select(p => new StoreGameSuggestion(p.ProductId!, p.Title!))
            .ToList();

    internal static string? PickSearchResult(string json, string name)
    {
        var games = (JsonSerializer.Deserialize(json, GamePassJsonContext.Default.AutosuggestResponseDto)?.Results ?? [])
            .SelectMany(r => r.Products ?? [])
            .Where(p => p.Type == "Game" && p.Title is not null)
            .ToList();
        // The exact name first (of two editions, the one named like the game),
        // then the game's own title, as the catalog matcher: the Store sells
        // "Control Ultimate Edition - Xbox Series X|S". Never a sequel ("Hades II").
        string wanted = CatalogMatcher.NormalizeName(name);
        string wantedKey = GameTitle.Key(name);
        return (games.FirstOrDefault(p => CatalogMatcher.NormalizeName(p.Title!) == wanted)
            ?? games.FirstOrDefault(p => wantedKey.Length > 0 && GameTitle.Key(p.Title!) == wantedKey))?
            .ProductId;
    }

    /// <summary>Fetches the genres and play modes of any Store products, by batches of 200.</summary>
    /// <returns>Features per product id; products without genres are left out.</returns>
    public async Task<IReadOnlyDictionary<string, ProductFeatures>> FetchFeaturesAsync(
        IEnumerable<string> productIds, CancellationToken ct = default)
    {
        var features = new Dictionary<string, ProductFeatures>();
        foreach (var batch in productIds.Distinct().Chunk(BatchSize))
        {
            string json = await GetStringAsync(ProductsUrlFor(batch), ct).ConfigureAwait(false);
            foreach (var product in ParseProducts(json))
            {
                var genres = GetGenres(product.Properties);
                if (product.ProductId is { } id && genres.Count > 0)
                    features[id] = new ProductFeatures(genres, GetModes(product.Properties));
            }
        }
        return features;
    }

    /// <summary>
    /// Translates one Store attribute into a play mode. The Store has several
    /// names for the same mode (old and new, cross-platform variants);
    /// technical capabilities (4K, cloud saves...) map to None.
    /// </summary>
    public static PlayModes ModeOf(string attribute) => attribute switch
    {
        "SinglePlayer" => PlayModes.Solo,
        "XblOnlineCoop" or "XblCrossPlatformCoop" or "CoopSupportOnline" or "CrossPlatformCoOp"
            => PlayModes.CoopOnline,
        "XblOnlineMultiPlayer" or "XblCrossPlatformMultiPlayer" or "OnlineMultiplayerWithGold" or "CrossPlatformMultiPlayer"
            => PlayModes.MultiOnline,
        "XblLocalCoop" or "XblLocalMultiPlayer" or "SharedSplitScreen" or "LocalMultiplayer" or "CoopSupportLocal"
            => PlayModes.Local,
        _ => PlayModes.None
    };

    // Combines the modes of every attribute with a bitwise OR.
    private static PlayModes GetModes(ProductPropertiesDto? properties) =>
        (properties?.Attributes ?? [])
            .Select(a => a.Name is null ? PlayModes.None : ModeOf(a.Name))
            .Aggregate(PlayModes.None, (all, mode) => all | mode);

    private string ProductsUrlFor(IEnumerable<string> ids) =>
        $"{ProductsUrl}?bigIds={string.Join(',', ids)}&market={_market}&languages={_language}";

    private static List<ProductDto> ParseProducts(string json) =>
        JsonSerializer.Deserialize(json, GamePassJsonContext.Default.ProductsResponseDto)?.Products ?? [];

    private async Task<IReadOnlyList<string>> FetchListAsync(string listId, CancellationToken ct)
    {
        string url = $"{ListsUrl}?id={listId}&language={_language}&market={_market}";
        return ParseListJson(await GetStringAsync(url, ct).ConfigureAwait(false));
    }

    private async Task<IReadOnlyList<string>> FetchPlanListAsync(PlanList list, CancellationToken ct)
    {
        string platform = list.Pc ? "pc" : "ConsoleGen8;ConsoleGen9";
        string url = $"{PlanListsUrl}?id={list.ListId}&language={_language}&market={_market}"
            + $"&platformContext={Uri.EscapeDataString(platform)}&subscriptionContext={list.Subscription}";
        // Same format as the v2 lists: a header, then the product ids.
        return ParseListJson(await GetStringAsync(url, ct).ConfigureAwait(false));
    }

    /// <summary>Extracts the product ids of a Game Pass list, skipping its header.</summary>
    public static IReadOnlyList<string> ParseListJson(string json) =>
        (JsonSerializer.Deserialize(json, GamePassJsonContext.Default.ListSiglEntryDto) ?? [])
            // The header has a siglId and no id: filtering on id skips it
            // without relying on its position.
            .Select(e => e.Id)
            .OfType<string>()
            .Where(id => id.Length > 0)
            .ToList();

    /// <summary>
    /// Turns a Store catalog answer into games. The id sets tell on which
    /// platforms each game is available and whether it is leaving soon; the
    /// dictionaries, which plans include it on each platform.
    /// </summary>
    public static IEnumerable<CatalogGame> ParseProductsJson(
        string json,
        IReadOnlySet<string> pcIds,
        IReadOnlySet<string> consoleIds,
        IReadOnlySet<string> leavingIds,
        IReadOnlyDictionary<string, GamePassTier>? consoleTiers = null,
        IReadOnlyDictionary<string, GamePassTier>? pcTiers = null)
    {
        foreach (var product in ParseProducts(json))
        {
            string? title = product.LocalizedProperties?.FirstOrDefault()?.ProductTitle?.Trim();
            if (string.IsNullOrEmpty(product.ProductId) || string.IsNullOrEmpty(title))
                continue;

            // "AllTime" rather than recent periods: more ratings, steadier average.
            var rating = product.MarketProperties?.FirstOrDefault()?.UsageData?
                .FirstOrDefault(u => u.AggregateTimeSpan == "AllTime");
            int ratingCount = rating?.RatingCount ?? 0;

            yield return new CatalogGame(
                ProductId: product.ProductId,
                Title: title,
                XboxTitleId: product.AlternateIds?
                    .FirstOrDefault(a => a.IdType == "XboxTitleId")?.Value,
                PackageFamilyName: string.IsNullOrWhiteSpace(product.Properties?.PackageFamilyName)
                    ? null
                    : product.Properties.PackageFamilyName,
                Genres: GetGenres(product.Properties),
                OnPc: pcIds.Contains(product.ProductId),
                OnConsole: consoleIds.Contains(product.ProductId),
                LeavingSoon: leavingIds.Contains(product.ProductId),
                AverageRating: ratingCount > 0 ? rating!.AverageRating : null,
                RatingCount: ratingCount,
                Modes: GetModes(product.Properties),
                ImageUrl: PickImageUrl(product.LocalizedProperties?.FirstOrDefault()?.Images),
                ConsoleTiers: consoleTiers?.GetValueOrDefault(product.ProductId) ?? GamePassTier.None,
                PcTiers: pcTiers?.GetValueOrDefault(product.ProductId) ?? GamePassTier.None);
        }
    }

    // Best picture for a wide card first: the 16:9 art with the game's title
    // is the most recognizable, then the same art without title, then the
    // square box art (cropped by the view).
    private static readonly string[] ImagePreference = ["TitledHeroArt", "SuperHeroArt", "BoxArt"];

    /// <summary>Full URL of the best card picture, or null if the product has none of the wanted kinds.</summary>
    internal static string? PickImageUrl(IEnumerable<ImageDto>? images)
    {
        var list = images?.Where(i => !string.IsNullOrWhiteSpace(i.Uri)).ToList() ?? [];

        string? uri = ImagePreference
            .Select(purpose => list.FirstOrDefault(i => i.ImagePurpose == purpose)?.Uri)
            .FirstOrDefault(u => u is not null);

        // The Store sends protocol-relative URLs ("//store-images...").
        return uri?.StartsWith("//", StringComparison.Ordinal) == true ? "https:" + uri : uri;
    }

    // Main category first, then the secondary ones, without duplicates.
    private static IReadOnlyList<string> GetGenres(ProductPropertiesDto? properties) =>
        new[] { properties?.Category }
            .Concat(properties?.Categories ?? [])
            .OfType<string>()
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<string> GetStringAsync(string url, CancellationToken ct) =>
        await GetStringOrNullAsync(url, ct, notFoundIsNull: false).ConfigureAwait(false)
        // Unreachable: without notFoundIsNull, errors throw instead of returning null.
        ?? throw new GamePassException(CoreStrings.GamePass_EmptyAnswer);

    /// <param name="notFoundIsNull">
    /// Returns null on HTTP 404 instead of throwing, and on 400: an id the
    /// Store cannot read (an odd title id) is just not a product, every time.
    /// </param>
    private async Task<string?> GetStringOrNullAsync(string url, CancellationToken ct, bool notFoundIsNull)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var outcome = HttpOutcomes.Classify(response);
        if (notFoundIsNull && outcome is HttpOutcome.NotFound or HttpOutcome.BadRequest)
            return null;
        if (outcome != HttpOutcome.Ok)
            throw new GamePassException(
                CoreText.Format(CoreStrings.GamePass_HttpError, (int)response.StatusCode, response.ReasonPhrase));

        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>A game found by the Microsoft Store search: its product id and its title (an edition may be in it).</summary>
public sealed record StoreGameSuggestion(string ProductId, string Title);
