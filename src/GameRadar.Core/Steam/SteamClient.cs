using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using GameRadar.Core.Services;

namespace GameRadar.Core.Steam;

/// <summary>A game of the Steam library, before its achievements are known.</summary>
internal sealed record SteamLibraryGame(string AppId, string Name, int PlaytimeMinutes, DateTimeOffset? LastPlayed, bool HasStats);

/// <summary>Achievements of one game: unlocked out of total.</summary>
internal sealed record SteamAchievements(int Unlocked, int Total);

/// <summary>
/// Connector for the Steam Web API (https://steamcommunity.com/dev), called
/// with the user's own key. Its only job: call the API and translate the
/// answers. Quota: 100,000 calls a day, far above our needs.
/// </summary>
// "partial": the regex source generator adds the body of the regex methods.
public sealed partial class SteamClient
{
    /// <summary>Value of <see cref="Models.PlayedGame.Platform"/> for games from this connector.</summary>
    public const string PlatformName = "Steam";

    private const string BaseUrl = "https://api.steampowered.com/";

    private readonly HttpClient _http;
    private readonly string _apiKey;

    public SteamClient(HttpClient http, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("A Steam Web API key is required.", nameof(apiKey));

        _http = http;
        _apiKey = apiKey.Trim();
    }

    /// <summary>
    /// SteamID64 of a profile given as the user copied it: profile URL
    /// (".../profiles/7656..." or ".../id/name"), bare SteamID64 or custom name.
    /// Costs 1 request only for a custom name.
    /// </summary>
    public async Task<string> ResolveSteamIdAsync(string profile, CancellationToken ct = default)
    {
        var (steamId, customName) = ParseProfile(profile);
        if (steamId is not null)
            return steamId;
        if (customName is null)
            throw new SteamException(CoreStrings.Steam_PasteProfile);

        var answer = await GetAsync(
            $"ISteamUser/ResolveVanityURL/v1/?vanityurl={Uri.EscapeDataString(customName)}",
            SteamJsonContext.Default.ResolveVanityEnvelope, ct).ConfigureAwait(false);
        return answer?.Response is { Success: 1, SteamId: { Length: > 0 } id }
            ? id
            : throw new SteamException(CoreText.Format(CoreStrings.Steam_NoSuchProfile, customName));
    }

    /// <summary>Splits what the user pasted into a SteamID64 or a custom profile name.</summary>
    internal static (string? SteamId, string? CustomName) ParseProfile(string profile)
    {
        string text = profile.Trim().TrimEnd('/');
        if (SteamIdPattern().Match(text) is { Success: true } id)
            return (id.Groups[1].Value, null);
        if (CustomUrlPattern().Match(text) is { Success: true } custom)
            return (null, custom.Groups[1].Value);
        // A bare custom name ("tonpseudo"), but not some other URL.
        return text.Length > 0 && !text.Contains('/') && !text.Contains(' ') ? (null, text) : (null, null);
    }

    /// <summary>
    /// The profile's display name, or null if Steam does not say. Throws
    /// <see cref="SteamException"/> when no profile has this SteamID64 (a
    /// typo in the number: Steam answers, with no player).
    /// </summary>
    public async Task<string?> FetchPersonaNameAsync(string steamId, CancellationToken ct = default)
    {
        var answer = await GetAsync(
            $"ISteamUser/GetPlayerSummaries/v2/?steamids={steamId}", SteamJsonContext.Default.PlayerSummariesEnvelope, ct).ConfigureAwait(false);
        return answer?.Response?.Players?.FirstOrDefault() is { } player
            ? player.PersonaName
            : throw new SteamException(CoreText.Format(CoreStrings.Steam_NoSuchSteamId, steamId));
    }

    /// <summary>
    /// The whole library (1 request) as raw JSON, to be cached as-is and read
    /// with <see cref="ParseLibrary"/>: play time and last play date included.
    /// </summary>
    public async Task<string> FetchLibraryJsonAsync(string steamId, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            $"IPlayerService/GetOwnedGames/v1/?steamid={steamId}&include_appinfo=1&include_played_free_games=1", ct).ConfigureAwait(false);
        EnsureSuccess(response);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns the raw library JSON into games. Throws <see cref="SteamException"/>
    /// when the profile hides its games, <see cref="JsonException"/> on invalid JSON.
    /// </summary>
    internal static IReadOnlyList<SteamLibraryGame> ParseLibrary(string json)
    {
        var response = JsonSerializer.Deserialize(json, SteamJsonContext.Default.OwnedGamesEnvelope)?.Response;

        // A private profile answers an empty object: no count, no games.
        if (response?.GameCount is null)
            throw new SteamException(
                CoreStrings.Steam_PrivateLibrary);

        return (response.Games ?? [])
            .Where(g => g.AppId > 0 && !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new SteamLibraryGame(
                g.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                g.Name!.Trim(),
                g.PlaytimeForever,
                g.LastPlayedUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(g.LastPlayedUnix) : null,
                g.HasCommunityVisibleStats))
            .ToList();
    }

    /// <summary>
    /// The whole wishlist (1 request) as raw JSON, to be cached as-is and read
    /// with <see cref="ParseWishlist"/>.
    /// </summary>
    public async Task<string> FetchWishlistJsonAsync(string steamId, CancellationToken ct = default)
    {
        using var response = await SendAsync($"IWishlistService/GetWishlist/v1/?steamid={steamId}", ct).ConfigureAwait(false);
        EnsureSuccess(response);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The Steam app ids of the wishlist. A list that is empty or hidden by
    /// the profile answers no item: both give an empty set, never an error
    /// that would stop a sync. Throws <see cref="JsonException"/> on invalid JSON.
    /// </summary>
    internal static IReadOnlySet<string> ParseWishlist(string json) =>
        (JsonSerializer.Deserialize(json, SteamJsonContext.Default.WishlistEnvelope)?.Response?.Items ?? [])
            .Where(item => item.AppId > 0)
            .Select(item => item.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet();

    /// <summary>
    /// Achievements of one game (1 request), or null when the game has none
    /// (or hides them). A refused key or the rate limit throw <see cref="SteamException"/>;
    /// a server error <see cref="HttpRequestException"/>, about this game only.
    /// </summary>
    internal async Task<SteamAchievements?> FetchAchievementsAsync(string steamId, string appId, CancellationToken ct = default)
    {
        using var response = await SendAsync(
            $"ISteamUserStats/GetPlayerAchievements/v1/?steamid={steamId}&appid={appId}", ct).ConfigureAwait(false);

        // 400 "Requested app has no stats", 403 "Profile is not public"...:
        // this game simply gives no achievement signal. (A refused key would
        // also be a 403, but it already failed on the library, fetched first.)
        var outcome = HttpOutcomes.Classify(response);
        if (outcome is HttpOutcome.BadRequest or HttpOutcome.Forbidden or HttpOutcome.NotFound)
            return null;
        // Some games answer 500 every time: an error about this game only,
        // not a refusal of the whole sync (the caller skips the game).
        if (outcome == HttpOutcome.ServerError)
            response.EnsureSuccessStatusCode();
        EnsureSuccess(response);

        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseAchievements(json);
    }

    internal static SteamAchievements? ParseAchievements(string json)
    {
        var stats = JsonSerializer.Deserialize(json, SteamJsonContext.Default.PlayerAchievementsEnvelope)?.PlayerStats;
        if (stats is not { Success: true, Achievements.Count: > 0 } || stats.Achievements is not { } list)
            return null;
        return new SteamAchievements(list.Count(a => a.Achieved == 1), list.Count);
    }

    private async Task<T?> GetAsync<T>(string pathAndQuery, JsonTypeInfo<T> typeInfo, CancellationToken ct) where T : class
    {
        using var response = await SendAsync(pathAndQuery, ct).ConfigureAwait(false);
        EnsureSuccess(response);
        string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, typeInfo);
    }

    // Steam wants the key in the query string. The URL is never shown or
    // logged, so the key does not leak into error messages.
    private Task<HttpResponseMessage> SendAsync(string pathAndQuery, CancellationToken ct) =>
        _http.GetAsync($"{BaseUrl}{pathAndQuery}&key={Uri.EscapeDataString(_apiKey)}", ct);

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        var outcome = HttpOutcomes.Classify(response);
        if (outcome == HttpOutcome.Ok)
            return;

        throw outcome switch
        {
            HttpOutcome.Unauthorized or HttpOutcome.Forbidden =>
                new SteamException(CoreStrings.Steam_KeyRefused) { IsKeyRefused = true },
            HttpOutcome.RateLimited =>
                new SteamException(CoreStrings.Steam_RateLimited) { IsRateLimited = true },
            _ =>
                new SteamException(CoreText.Format(CoreStrings.Steam_HttpError, (int)response.StatusCode, response.ReasonPhrase))
        };
    }

    // SteamID64: 17 digits starting with 7656, bare or in a /profiles/ URL
    // (maybe of a subpage, or with a query: ".../games/?tab=all").
    [GeneratedRegex(@"^(?:.*/profiles/)?(7656\d{13})(?:[/?#].*)?$")]
    private static partial Regex SteamIdPattern();

    // Custom URL: steamcommunity.com/id/<name>, maybe of a subpage or with a query.
    [GeneratedRegex(@"steamcommunity\.com/id/([^/?#]+)(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex CustomUrlPattern();
}
