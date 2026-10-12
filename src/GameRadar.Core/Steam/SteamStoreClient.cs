using System.Text.Json;
using System.Text.Json.Serialization;
using GameRadar.Core.GamePass;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.Steam;

/// <summary>
/// The Steam store's game pages (store.steampowered.com/api/appdetails, no
/// key): genres and categories of a game, translated into the Microsoft
/// Store genres and the play modes used everywhere else in the app.
/// Last resort, for games the Microsoft Store does not know: Steam genres
/// are coarse (no "Shooter": shooters are just "Action").
/// Rate limited by Steam (~200 requests per 5 minutes): callers go slowly.
/// </summary>
public sealed class SteamStoreClient
{
    private const string AppDetailsUrl = "https://store.steampowered.com/api/appdetails";


    private readonly HttpClient _http;

    public SteamStoreClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// Genres and modes of one game (1 request), or null when Steam has no
    /// page for it or no genre we can translate. Throws <see cref="SteamException"/>
    /// on the rate limit or a server error, so the caller stops and retries
    /// another day: neither means the game has no page.
    /// </summary>
    public async Task<ProductFeatures?> FetchFeaturesAsync(string appId, CancellationToken ct = default)
    {
        // English names: stable keys, whatever the user's language.
        using var response = await _http.GetAsync(
            $"{AppDetailsUrl}?appids={Uri.EscapeDataString(appId)}&l=english&filters=genres,categories", ct).ConfigureAwait(false);

        switch (HttpOutcomes.Classify(response))
        {
            case HttpOutcome.Ok:
                break;
            case HttpOutcome.RateLimited or HttpOutcome.Forbidden:
                throw new SteamException(CoreStrings.Steam_StoreRateLimitedGenres);
            case HttpOutcome.NotFound:
                return null;
            default:
                // Steam down: not "no page", which would be remembered for 30 days.
                throw new SteamException(CoreStrings.Steam_StoreDownGenres);
        }

        return ParseFeatures(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), appId);
    }

    internal static ProductFeatures? ParseFeatures(string json, string appId)
    {
        var pages = JsonSerializer.Deserialize(json, SteamJsonContext.Default.DictionaryStringAppDetailsDto);
        // "data" is an object, or an empty ARRAY for a game with neither
        // genres nor categories (an object would not deserialize from it).
        if (pages?.GetValueOrDefault(appId) is not { Success: true, Data: { ValueKind: JsonValueKind.Object } element }
            || element.Deserialize(SteamJsonContext.Default.AppDataDto) is not { } data)
            return null;

        // Main genre first; "Action" and "Adventure" both become
        // "Action & adventure", kept once.
        var genres = (data.Genres ?? [])
            .Select(g => g.Description is null ? null : GenreOf(g.Description))
            .OfType<string>()
            .Distinct()
            .ToList();
        if (genres.Count == 0)
            return null;

        var modes = (data.Categories ?? [])
            .Select(c => c.Description is null ? PlayModes.None : ModeOf(c.Description))
            .Aggregate(PlayModes.None, (all, mode) => all | mode);
        return new ProductFeatures(genres, modes);
    }

    /// <summary>
    /// Steam genre → Microsoft Store genre, or null when it is not a genre
    /// but a business model or a status ("Indie", "Free To Play",
    /// "Massively Multiplayer", "Early Access") or too vague ("Casual").
    /// </summary>
    public static string? GenreOf(string steamGenre) => steamGenre switch
    {
        "Action" or "Adventure" => "Action & adventure",
        "RPG" => "Role playing",
        "Strategy" => "Strategy",
        "Simulation" => "Simulation",
        "Racing" => "Racing & flying",
        "Sports" => "Sports",
        _ => null
    };

    /// <summary>
    /// Steam category → play mode. Generic categories ("Multi-player",
    /// "Co-op", "PvP") do not say online or local: ignored.
    /// </summary>
    public static PlayModes ModeOf(string category) => category switch
    {
        "Single-player" => PlayModes.Solo,
        "Online Co-op" => PlayModes.CoopOnline,
        "Online PvP" or "MMO" or "Cross-Platform Multiplayer" => PlayModes.MultiOnline,
        "Shared/Split Screen" or "Shared/Split Screen Co-op" or "Shared/Split Screen PvP"
            or "LAN Co-op" or "LAN PvP" or "Remote Play Together" => PlayModes.Local,
        _ => PlayModes.None
    };

    // { "<appid>": { "success": true, "data": { "genres": [...], "categories": [...] } } }
    internal sealed class AppDetailsDto
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }
    }

    internal sealed class AppDataDto
    {
        [JsonPropertyName("genres")]
        public List<DescriptionDto>? Genres { get; set; }

        [JsonPropertyName("categories")]
        public List<DescriptionDto>? Categories { get; set; }
    }

    internal sealed class DescriptionDto
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
