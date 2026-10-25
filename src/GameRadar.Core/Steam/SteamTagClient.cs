using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GameRadar.Core.Services;

namespace GameRadar.Core.Steam;

/// <summary>
/// The Steam store's user tags ("Looter Shooter", "Souls-like"...) of any
/// game, by app id or by title, through public APIs (no key). Tags are far
/// finer than the Store genres: they are what the similarity search compares.
/// A game's tags: tag id → vote weight relative to its most voted tag (0 to 1).
/// </summary>
public sealed class SteamTagClient
{
    internal const string GetItemsUrl = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/";
    private const string SearchUrl = "https://api.steampowered.com/IStoreQueryService/SearchSuggestions/v1/";
    private const string TagListUrl = "https://api.steampowered.com/IStoreService/GetTagList/v1/";

    /// <summary>App ids per GetItems request: 300 is refused (URL too long).</summary>
    public const int BatchSize = 100;

    // Tags voted on a game, at most; the API sorts them by votes.
    private const int TagCount = 20;

    // Business model, release status or content warning, not what the game
    // is like: 113 Free to Play, 492 Indie, 493 Early Access, 6650 Nudity,
    // 9130 Hentai, 12095 Sexual Content (on The First Descendant, the top tag).
    private static readonly HashSet<int> NonStyleTags = [113, 492, 493, 6650, 9130, 12095];


    private readonly HttpClient _http;

    public SteamTagClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>Tags of up to <see cref="BatchSize"/> Steam games (1 request). Games without tags are left out.</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<int, double>>> FetchByAppIdsAsync(
        IReadOnlyCollection<string> appIds, CancellationToken ct = default)
    {
        if (appIds.Count > BatchSize)
            throw new ArgumentException($"At most {BatchSize} app ids per request.", nameof(appIds));

        var input = new GetItemsRequest
        {
            Ids = appIds.Select(id => new AppIdDto { AppId = int.Parse(id, System.Globalization.CultureInfo.InvariantCulture) }).ToList(),
            DataRequest = new DataRequestDto { IncludeTagCount = TagCount }
        };
        string json = await GetAsync(GetItemsUrl, input, SteamJsonContext.Default.GetItemsRequest, ct).ConfigureAwait(false);
        return ParseItems(json)
            .Where(item => item.Tags.Count > 0)
            .GroupBy(item => item.AppId)
            .ToDictionary(g => g.Key, g => g.First().Tags);
    }

    /// <summary>
    /// The Steam game with exactly this title (once cleaned of edition and
    /// platform words), or null when Steam has none. 1 request.
    /// </summary>
    public async Task<(string AppId, IReadOnlyDictionary<int, double> Tags)?> SearchByTitleAsync(string title, CancellationToken ct = default)
    {
        string wanted = GameTitle.Clean(title);
        if (wanted.Length == 0)
            return null;

        var input = new SearchRequest
        {
            SearchTerm = wanted,
            MaxResults = 5,
            DataRequest = new DataRequestDto { IncludeTagCount = TagCount }
        };
        string json = await GetAsync(SearchUrl, input, SteamJsonContext.Default.SearchRequest, ct).ConfigureAwait(false);
        return FindExactTitle(ParseItems(json), wanted);
    }

    /// <summary>Tag id → display name in the given Steam language ("french"). 1 request.</summary>
    public async Task<IReadOnlyDictionary<int, string>> FetchTagNamesAsync(string language, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"{TagListUrl}?language={Uri.EscapeDataString(language)}", ct).ConfigureAwait(false);
        ThrowIfRefused(response);
        return SteamCatalogClient.ParseTagList(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    private async Task<string> GetAsync<T>(string url, T input, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        using var response = await _http.GetAsync(SteamCatalogClient.InputUrl(url, input, typeInfo), ct).ConfigureAwait(false);
        ThrowIfRefused(response);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static void ThrowIfRefused(HttpResponseMessage response) =>
        SteamCatalogClient.ThrowIfRefused(response, CoreStrings.Steam_StoreRateLimitedTags, CoreStrings.Steam_StoreDownTags);

    /// <summary>
    /// The games whose title matches what the player types, for "Ajouter un
    /// jeu": Steam's own search, best matches first, DLCs and soundtracks
    /// left out. 1 request, no key.
    /// </summary>
    public async Task<IReadOnlyList<SteamSuggestion>> SuggestGamesAsync(string term, int max = 8, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term))
            return [];

        var input = new SearchRequest
        {
            SearchTerm = term.Trim(),
            MaxResults = max,
            // Without a data request, the answer only lists ids, no names.
            DataRequest = new DataRequestDto { IncludeTagCount = TagCount }
        };
        return ParseGames(await GetAsync(SearchUrl, input, SteamJsonContext.Default.SearchRequest, ct).ConfigureAwait(false));
    }

    internal static IReadOnlyList<SteamSuggestion> ParseGames(string json)
    {
        var items = JsonSerializer.Deserialize(json, SteamJsonContext.Default.ItemsEnvelope)?.Response?.StoreItems ?? [];
        return items
            .Where(i => i is { Success: 1, AppId: not null, Name: not null, Type: 0 or null })
            .Select(i => new SteamSuggestion(
                i.AppId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                i.Name!))
            .ToList();
    }

    /// <summary>Items of a GetItems or SearchSuggestions answer: app id, name, tags.</summary>
    internal static IReadOnlyList<(string AppId, string Name, IReadOnlyDictionary<int, double> Tags)> ParseItems(string json)
    {
        var items = JsonSerializer.Deserialize(json, SteamJsonContext.Default.ItemsEnvelope)?.Response?.StoreItems ?? [];
        return items
            .Where(i => i is { Success: 1, AppId: not null, Name: not null })
            .Select(i => (
                i.AppId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                i.Name!,
                ToTagWeights((i.Tags ?? []).Select(t => (t.TagId, t.Weight)))))
            .ToList();
    }

    /// <summary>
    /// Vote weights relative to the most voted tag (1 = most voted), without
    /// the business tags. Relative, so that a hugely popular game and a
    /// small one are described on the same scale.
    /// </summary>
    internal static IReadOnlyDictionary<int, double> ToTagWeights(IEnumerable<(int TagId, int Weight)> tags)
    {
        var kept = tags.Where(t => t.Weight > 0 && !NonStyleTags.Contains(t.TagId)).ToList();
        if (kept.Count == 0)
            return new Dictionary<int, double>();

        double max = kept.Max(t => t.Weight);
        return kept
            .GroupBy(t => t.TagId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.Weight) / max);
    }

    /// <summary>A tag that says what the game is like (not "Free to Play", "Early Access"...).</summary>
    internal static bool IsStyle(int tagId) => !NonStyleTags.Contains(tagId);

    /// <summary>
    /// Same tags without the non-style ones: applied when reading as well,
    /// so tags cached before a tag joined the list are cleaned too.
    /// </summary>
    internal static IReadOnlyDictionary<int, double> StyleOnly(IReadOnlyDictionary<int, double> tags) =>
        tags.Keys.Any(NonStyleTags.Contains)
            ? tags.Where(t => !NonStyleTags.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value)
            : tags;

    internal static (string AppId, IReadOnlyDictionary<int, double> Tags)? FindExactTitle(
        IEnumerable<(string AppId, string Name, IReadOnlyDictionary<int, double> Tags)> items, string cleanedTitle)
    {
        string wanted = GameTitle.Key(cleanedTitle);
        foreach (var item in items)
        {
            // Exact: "Hades" must not take the tags of "Hades II".
            if (item.Tags.Count > 0 && GameTitle.Key(item.Name) == wanted)
                return (item.AppId, item.Tags);
        }
        return null;
    }

    // ---------- DTOs: GetItems / SearchSuggestions (same item shape) ----------

    internal sealed class ItemsEnvelope
    {
        [JsonPropertyName("response")]
        public ItemsDto? Response { get; set; }
    }

    internal sealed class ItemsDto
    {
        [JsonPropertyName("store_items")]
        public List<ItemDto>? StoreItems { get; set; }
    }

    internal sealed class ItemDto
    {
        [JsonPropertyName("success")]
        public int Success { get; set; }

        [JsonPropertyName("appid")]
        public int? AppId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>0 = a game; 4 = a DLC, 3 a soundtrack... (seen on the real API).</summary>
        [JsonPropertyName("type")]
        public int? Type { get; set; }

        [JsonPropertyName("tags")]
        public List<TagWeightDto>? Tags { get; set; }
    }
}

/// <summary>A game found by the Steam store search: its app id and its name.</summary>
public sealed record SteamSuggestion(string AppId, string Name);
