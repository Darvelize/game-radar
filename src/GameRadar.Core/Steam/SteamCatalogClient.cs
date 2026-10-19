using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.Steam;

/// <summary>
/// Candidate games from the Steam store, beyond the Game Pass: the best
/// sellers, with tags, reviews and pictures (IStoreQueryService/Query, public,
/// no key: works for players without a Steam account too). Tags are
/// translated into the Microsoft Store genres, the vocabulary of the taste
/// profile. 3 requests in all: the tag names, then 2 pages of 1,000 games.
/// </summary>
public sealed class SteamCatalogClient
{
    private const string TagListUrl = "https://api.steampowered.com/IStoreService/GetTagList/v1/?language=english";
    private const string QueryUrl = "https://api.steampowered.com/IStoreQueryService/Query/v1/";
    private const string AssetBaseUrl = "https://shared.akamai.steamstatic.com/store_item_assets/";

    /// <summary>Games per request: the most the API gives in one page.</summary>
    public const int PageSize = 1000;
    public const int PageCount = 2;

    // Store sort order "top sellers": recent hits and long-time favorites
    // (Warframe, Destiny 2...). The API has no "best rated" order.
    private const int SortTopSellers = 11;

    /// <summary>Fewer reviews: too little known to recommend.</summary>
    public const int MinReviewCount = 500;

    /// <summary>
    /// Below 70 % of positive reviews ("Mixed" on Steam): not recommended,
    /// whatever the genre. ~73 % of the best sellers pass both bars.
    /// </summary>
    public const int MinPositivePercent = 70;

    // Steam content descriptors: 3 = adult only sexual content, 4 = frequent
    // nudity or sexual content. Left out: the app is shared.
    private static readonly int[] AdultDescriptors = [3, 4];

    // Only the most voted tags describe the game; the tail is noise.
    private const int TagsConsidered = 8;
    private const int MaxGenres = 3;


    private readonly HttpClient _http;

    public SteamCatalogClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>Fetches the tag names then the best sellers, filtered and translated.</summary>
    public async Task<IReadOnlyList<CatalogGame>> FetchCatalogAsync(CancellationToken ct = default)
    {
        var tagNames = ParseTagList(await GetStringAsync(_http, TagListUrl, ct).ConfigureAwait(false));

        var pages = await TopSellerPagesAsync(_http,
            new DataRequestDto { IncludeTagCount = 20, IncludeReviews = true, IncludeAssets = true }, PageCount, ct).ConfigureAwait(false);
        // A game may move from one page to the next between the two requests.
        return pages.SelectMany(json => ParseQuery(json, tagNames)).DistinctBy(g => g.ProductId).ToList();
    }

    /// <summary>The first <paramref name="pageCount"/> pages of best sellers, asked at once, in their order.</summary>
    internal static Task<string[]> TopSellerPagesAsync(HttpClient http, DataRequestDto data, int pageCount, CancellationToken ct) =>
        Task.WhenAll(Enumerable.Range(0, pageCount).Select(page => GetStringAsync(http, TopSellersUrl(page * PageSize, data), ct)));

    internal static async Task<string> GetStringAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (HttpOutcomes.Classify(response) != HttpOutcome.Ok)
            throw new SteamException(CoreStrings.Steam_StoreDown);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A page of games, best sellers first.</summary>
    internal static string TopSellersUrl(int start, DataRequestDto data, PriceFiltersDto? price = null) =>
        // The names of the JSON are the API's own (see CatalogQueryRequest).
        InputUrl(QueryUrl, new CatalogQueryRequest
        {
            Query = new CatalogQueryDto
            {
                Start = start,
                Count = PageSize,
                Sort = SortTopSellers,
                Filters = new CatalogFiltersDto { TypeFilters = new TypeFiltersDto { IncludeGames = true }, PriceFilters = price }
            },
            DataRequest = data
        }, SteamJsonContext.Default.CatalogQueryRequest);

    /// <summary>A store service's address with its request, the way they all take it (input_json).</summary>
    internal static string InputUrl<T>(string url, T input, JsonTypeInfo<T> typeInfo) =>
        $"{url}?input_json={Uri.EscapeDataString(JsonSerializer.Serialize(input, typeInfo))}";

    /// <summary>Tag id → English tag name.</summary>
    internal static IReadOnlyDictionary<int, string> ParseTagList(string json)
    {
        var answer = JsonSerializer.Deserialize(json, SteamJsonContext.Default.TagListEnvelope);
        var tags = answer?.Response?.Tags;
        if (tags is null || tags.Count == 0)
            throw new SteamException(CoreStrings.Steam_EmptyTagList);

        return tags
            .Where(t => t.Name is not null)
            .GroupBy(t => t.TagId)
            .ToDictionary(g => g.Key, g => g.First().Name!);
    }

    /// <summary>One page of games, minus the little known, the poorly rated, the adult and the untranslatable.</summary>
    internal static IReadOnlyList<CatalogGame> ParseQuery(string json, IReadOnlyDictionary<int, string> tagNames)
    {
        var items = JsonSerializer.Deserialize(json, SteamJsonContext.Default.QueryEnvelope)?.Response?.StoreItems;
        if (items is null)
            throw new SteamException(CoreStrings.Steam_StoreFormat);

        var games = new List<CatalogGame>();
        foreach (var item in items)
        {
            if (item is not { Success: 1, AppId: int appId, Name: { Length: > 0 } name }
                || item.Reviews?.Summary is not { ReviewCount: >= MinReviewCount, PercentPositive: >= MinPositivePercent } reviews
                || IsAdult(item))
                continue;

            // Most voted tags first, translated; the first genre found is the main one.
            var genres = (item.Tags ?? [])
                .OrderByDescending(t => t.Weight)
                .Take(TagsConsidered)
                .Select(t => tagNames.GetValueOrDefault(t.TagId) is { } tag ? GenreOfTag(tag) : null)
                .OfType<string>()
                .Distinct()
                .Take(MaxGenres)
                .ToList();
            if (genres.Count == 0)
                continue;

            var modes = (item.Categories?.SupportedPlayerCategoryIds ?? [])
                .Aggregate(PlayModes.None, (all, id) => all | ModeOfCategory(id));

            string id = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            games.Add(new CatalogGame(
                ProductId: "steam:" + id,
                Title: name,
                XboxTitleId: null,
                PackageFamilyName: null,
                Genres: genres,
                OnPc: false,
                OnConsole: false,
                LeavingSoon: false,
                // Same scale as the Microsoft Store ratings: 87 % positive = 4.35 / 5.
                AverageRating: reviews.PercentPositive * 5 / 100.0,
                RatingCount: reviews.ReviewCount,
                Modes: modes,
                ImageUrl: ImageUrlOf(item.Assets),
                SteamAppId: id,
                // Kept for the similarity search (same scale as SteamTagClient).
                Tags: SteamTagClient.ToTagWeights((item.Tags ?? []).Select(t => (t.TagId, t.Weight)))));
        }
        return games;
    }

    /// <summary>A Steam game's header picture (460 × 215), from its app id alone.</summary>
    internal static string HeaderImageUrl(string appId) => AssetBaseUrl + "steam/apps/" + Uri.EscapeDataString(appId) + "/header.jpg";

    internal static bool IsAdult(StoreItemDto item) => item.ContentDescriptorIds?.Intersect(AdultDescriptors).Any() == true;

    // "main_capsule" is 616 × 353: close to the 16:9 of the cards.
    internal static string? ImageUrlOf(AssetsDto? assets) =>
        assets is { UrlFormat: { } format, MainCapsule: { } capsule }
            ? AssetBaseUrl + format.Replace("${FILENAME}", capsule, StringComparison.Ordinal)
            : null;

    /// <summary>
    /// Steam tag → Microsoft Store genre, or null for tags that are not a
    /// genre (setting, mood, business model: "Sci-fi", "Atmospheric",
    /// "Free to Play", "Indie"...). Much finer than Steam's own genres:
    /// "FPS" or "Looter Shooter" say "Shooter", where the genre is just "Action".
    /// </summary>
    public static string? GenreOfTag(string tag) => tag switch
    {
        "Shooter" or "FPS" or "Third-Person Shooter" or "Looter Shooter" or "Hero Shooter" or "Arena Shooter"
            or "Extraction Shooter" or "Boomer Shooter" or "Top-Down Shooter" or "Twin Stick Shooter"
            or "Shoot 'Em Up" or "Bullet Hell" or "Rail Shooter" or "On-Rails Shooter" or "Battle Royale" => "Shooter",
        "RPG" or "Action RPG" or "JRPG" or "CRPG" or "MMORPG" or "Party-Based RPG" or "Strategy RPG"
            or "Tactical RPG" or "Souls-like" or "Dungeon Crawler" => "Role playing",
        "Action" or "Adventure" or "Action-Adventure" or "Open World" or "Hack and Slash"
            or "Character Action Game" or "Stealth" or "Survival" or "Survival Horror" => "Action & adventure",
        "Strategy" or "RTS" or "Turn-Based Strategy" or "Grand Strategy" or "4X" or "Tower Defense"
            or "Real Time Tactics" or "Turn-Based Tactics" or "Wargame" or "Auto Battler" or "Action RTS" => "Strategy",
        "Simulation" or "City Builder" or "Colony Sim" or "Farming Sim" or "Life Sim" or "Space Sim"
            or "Automation" or "Management" or "Base Building" or "Job Simulator" or "Automobile Sim"
            or "Medical Sim" or "Hobby Sim" => "Simulation",
        "Racing" or "Driving" or "Flight" or "Combat Racing" or "Combat Flight Simulator" => "Racing & flying",
        "Sports" or "Football (Soccer)" or "Football (American)" or "Basketball" or "Baseball" or "Hockey"
            or "Golf" or "Tennis" or "Wrestling" or "Boxing" or "Skateboarding" or "Cycling" or "Rugby"
            or "Cricket" or "Volleyball" or "Skiing" or "Snowboarding" or "BMX" or "Bowling" => "Sports",
        "Platformer" or "2D Platformer" or "3D Platformer" or "Precision Platformer" or "Puzzle Platformer"
            or "Metroidvania" => "Platformer",
        "Fighting" or "2D Fighter" or "3D Fighter" or "Beat 'em up" => "Fighting",
        "Puzzle" or "Match 3" or "Hidden Object" or "Logic" or "Trivia" or "Word Game" or "Escape Room"
            or "Sokoban" => "Puzzle & trivia",
        "Card Game" or "Board Game" or "Deckbuilding" or "Roguelike Deckbuilder" or "Card Battler"
            or "Trading Card Game" or "Chess" or "Tabletop" or "Solitaire" or "Mahjong" or "Poker" => "Card & board",
        "MOBA" => "Multi-player Online Battle Arena",
        "Rhythm" => "Music",
        "Family Friendly" => "Family & kids",
        _ => null
    };

    /// <summary>
    /// Steam "supported players" category id → play mode, same rules as
    /// <see cref="SteamStoreClient.ModeOf"/> (which reads the labels). Generic
    /// ones (1 "Multi-player", 9 "Co-op", 49 "PvP") say neither online nor local.
    /// </summary>
    public static PlayModes ModeOfCategory(int id) => id switch
    {
        2 => PlayModes.Solo,                                  // Single-player
        38 => PlayModes.CoopOnline,                           // Online Co-op
        36 or 20 or 27 => PlayModes.MultiOnline,              // Online PvP, MMO, Cross-Platform Multiplayer
        24 or 37 or 39 or 47 or 48 or 44 => PlayModes.Local,  // Split screen (3 kinds), LAN PvP/Co-op, Remote Play Together
        _ => PlayModes.None
    };

    // ---------- DTOs: GetTagList ----------

    internal sealed class TagListEnvelope
    {
        [JsonPropertyName("response")]
        public TagListDto? Response { get; set; }
    }

    internal sealed class TagListDto
    {
        [JsonPropertyName("tags")]
        public List<TagNameDto>? Tags { get; set; }
    }

    internal sealed class TagNameDto
    {
        [JsonPropertyName("tagid")]
        public int TagId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    // ---------- DTOs: Query ----------

    internal sealed class QueryEnvelope
    {
        [JsonPropertyName("response")]
        public QueryDto? Response { get; set; }
    }

    internal sealed class QueryDto
    {
        [JsonPropertyName("store_items")]
        public List<StoreItemDto>? StoreItems { get; set; }
    }

    internal sealed class StoreItemDto
    {
        [JsonPropertyName("success")]
        public int Success { get; set; }

        [JsonPropertyName("appid")]
        public int? AppId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("content_descriptorids")]
        public List<int>? ContentDescriptorIds { get; set; }

        [JsonPropertyName("tags")]
        public List<TagWeightDto>? Tags { get; set; }

        [JsonPropertyName("categories")]
        public CategoriesDto? Categories { get; set; }

        [JsonPropertyName("reviews")]
        public ReviewsDto? Reviews { get; set; }

        [JsonPropertyName("assets")]
        public AssetsDto? Assets { get; set; }

        [JsonPropertyName("best_purchase_option")]
        public PurchaseOptionDto? BestPurchaseOption { get; set; }

        [JsonPropertyName("free_weekend")]
        public FreeWeekendDto? FreeWeekend { get; set; }
    }

    internal sealed class PurchaseOptionDto
    {
        // Given away: free now and kept for good.
        [JsonPropertyName("is_free_to_keep")]
        public bool IsFreeToKeep { get; set; }

        // Unix seconds.
        [JsonPropertyName("free_to_keep_ends")]
        public long? FreeToKeepEnds { get; set; }
    }

    internal sealed class FreeWeekendDto
    {
        // Unix seconds.
        [JsonPropertyName("start_time")]
        public long? StartTime { get; set; }

        [JsonPropertyName("end_time")]
        public long? EndTime { get; set; }
    }

    internal sealed class CategoriesDto
    {
        [JsonPropertyName("supported_player_categoryids")]
        public List<int>? SupportedPlayerCategoryIds { get; set; }
    }

    internal sealed class ReviewsDto
    {
        // "filtered": without the review bombing periods Steam detected.
        [JsonPropertyName("summary_filtered")]
        public ReviewSummaryDto? Summary { get; set; }
    }

    internal sealed class ReviewSummaryDto
    {
        [JsonPropertyName("review_count")]
        public int ReviewCount { get; set; }

        [JsonPropertyName("percent_positive")]
        public int PercentPositive { get; set; }
    }

    internal sealed class AssetsDto
    {
        // e.g. "steam/apps/230410/${FILENAME}?t=1790290779"
        [JsonPropertyName("asset_url_format")]
        public string? UrlFormat { get; set; }

        [JsonPropertyName("main_capsule")]
        public string? MainCapsule { get; set; }
    }
}
