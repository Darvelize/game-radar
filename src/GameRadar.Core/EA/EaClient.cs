using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using GameRadar.Core.Services;

namespace GameRadar.Core.EA;

/// <summary>
/// The games played in the EA app (EA Play included), their play time and
/// achievements, through the GraphQL API of the EA app (unofficial), with an
/// access token the user copies from EA's own page (valid 4 h, never saved).
/// </summary>
public sealed partial class EaClient
{
    /// <summary>The platform name of EA games (a brand, never translated).</summary>
    public const string PlatformName = "EA";

    /// <summary>
    /// The page the user opens in their browser, signed in to ea.com: it shows
    /// the JSON with the access token. EA's own client id (ORIGIN_JS_SDK):
    /// EA registers no third-party app: no other way to read the history.
    /// </summary>
    public const string TokenPageUrl = "https://accounts.ea.com/connect/auth?client_id=ORIGIN_JS_SDK&response_type=token&redirect_uri=nucleus:rest&prompt=none";

    /// <summary>
    /// Slugs per recentGames request. Measured: beyond about ten, EA silently
    /// drops played games (500 at once gave 2 of 12); 5 and 10 both give all 12.
    /// </summary>
    public const int RecentGamesBatchSize = 5;

    private const string GraphQlUrl = "https://service-aggregation-layer.juno.ea.com/graphql";

    private const string IdentityQuery = "query { me { player { psd } } }";
    private const string GameSearchQuery = "query { gameSearch(filter: { gameTypes: [BASE_GAME, COLLECTION] }, paging: { limit: 9999 }) { items { slug } } }";
    private const string RecentGamesQuery = "query GetRecentGames($gameSlugs: [String!]!) { me { recentGames(gameSlugs: $gameSlugs) { items { gameSlug lastSessionEndDate totalPlayTimeSeconds } } } }";
    private const string GamesQuery = "query GetGamesBySlugs($slugs: [String!]!, $locale: Locale) { games(slugs: $slugs, locale: $locale) { items { slug products { items { name originOfferId baseItem { gameType } } } } } }";
    private const string AchievementsQuery = "query GetAchievements($offerId: String!, $playerPsd: String!, $locale: Locale!) { achievements(offerId: $offerId, playerPsd: $playerPsd, showHidden: true, locale: $locale) { achievements { awardCount } } }";

    private readonly HttpClient _http;
    private readonly string _token;

    public EaClient(HttpClient http, string token)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _token = token;
    }

    /// <summary>The player id the achievements are asked for.</summary>
    internal async Task<string> FetchPlayerIdAsync(CancellationToken ct = default) =>
        ParseIdentity(await PostAsync(new GraphQlRequest<NoVariables> { Query = IdentityQuery },
            EaJsonContext.Default.GraphQlRequestNoVariables, withToken: true, ct).ConfigureAwait(false));

    /// <summary>Every game EA knows (public: asked without the token).</summary>
    internal async Task<IReadOnlyList<string>> FetchGameSlugsAsync(CancellationToken ct = default) =>
        ParseGameSearch(await PostAsync(new GraphQlRequest<NoVariables> { Query = GameSearchQuery },
            EaJsonContext.Default.GraphQlRequestNoVariables, withToken: false, ct).ConfigureAwait(false));

    /// <summary>
    /// The raw answer for at most <see cref="RecentGamesBatchSize"/> slugs: the
    /// ones the player played, read with <see cref="ParseRecentGames"/> (kept raw in the cache).
    /// </summary>
    internal Task<string> FetchRecentGamesJsonAsync(IReadOnlyList<string> slugs, CancellationToken ct = default)
    {
        if (slugs.Count > RecentGamesBatchSize)
            throw new ArgumentException($"EA drops played games beyond a few slugs per request: at most {RecentGamesBatchSize}.", nameof(slugs));
        return PostAsync(new GraphQlRequest<GameSlugsVariables> { Query = RecentGamesQuery, Variables = new() { GameSlugs = slugs } },
            EaJsonContext.Default.GraphQlRequestGameSlugsVariables, withToken: true, ct);
    }

    /// <summary>The raw answer giving the base game product of each slug, read with <see cref="ParseGames"/> (kept raw in the cache).</summary>
    internal Task<string> FetchGamesJsonAsync(IReadOnlyList<string> slugs, CancellationToken ct = default) =>
        PostAsync(new GraphQlRequest<SlugsVariables> { Query = GamesQuery, Variables = new() { Slugs = slugs } },
            EaJsonContext.Default.GraphQlRequestSlugsVariables, withToken: true, ct);

    /// <summary>The player's achievements in a game, or null when it has none.</summary>
    internal async Task<EaAchievements?> FetchAchievementsAsync(string offerId, string playerId, CancellationToken ct = default) =>
        ParseAchievements(await PostAsync(new GraphQlRequest<AchievementsVariables> { Query = AchievementsQuery, Variables = new() { OfferId = offerId, PlayerPsd = playerId } },
            EaJsonContext.Default.GraphQlRequestAchievementsVariables, withToken: true, ct).ConfigureAwait(false));

    private async Task<string> PostAsync<T>(T body, JsonTypeInfo<T> typeInfo, bool withToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, typeInfo), System.Text.Encoding.UTF8, "application/json")
        };
        if (withToken)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        return HttpOutcomes.Classify(response) switch
        {
            HttpOutcome.Ok => await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false),
            // Measured: an unknown or expired token answers 401, empty.
            HttpOutcome.Unauthorized or HttpOutcome.Forbidden => throw new EaException(CoreStrings.Ea_TokenExpired) { IsTokenRefused = true },
            HttpOutcome.RateLimited => throw new EaException(CoreStrings.Ea_RateLimited) { IsRateLimited = true },
            _ => throw new EaException(CoreStrings.Ea_Down)
        };
    }

    /// <summary>
    /// The access token in what the user pasted: the whole JSON of EA's page,
    /// or the token alone. Throws <see cref="EaException"/> saying what to do.
    /// </summary>
    public static string RequireToken(string pasted)
    {
        string text = pasted.Trim();
        if (text.StartsWith('{'))
        {
            TokenPageDto? page;
            try
            {
                page = JsonSerializer.Deserialize(text, EaJsonContext.Default.TokenPageDto);
            }
            catch (JsonException)
            {
                throw new EaException(CoreStrings.Ea_PasteToken);
            }
            // The page of a browser signed out of ea.com: {"error":"login_required", ...}.
            if (page?.Error is not null)
                throw new EaException(CoreStrings.Ea_SignInFirst);
            text = page?.AccessToken ?? string.Empty;
        }
        return TokenPattern().IsMatch(text) ? text : throw new EaException(CoreStrings.Ea_PasteToken);
    }

    // EA's tokens: ~84 letters and digits (base64 of "AT1:..."), measured; base64 may end with "=" padding.
    [GeneratedRegex("^[A-Za-z0-9]{40,}={0,2}$")]
    private static partial Regex TokenPattern();

    /// <summary>The player id the achievements are asked for (me.player.psd).</summary>
    internal static string ParseIdentity(string json) =>
        Data(json, EaJsonContext.Default.GraphQlEnvelopeMeData).Me?.Player?.Psd
            ?? throw new EaException(CoreStrings.Ea_Format);

    /// <summary>Every game EA knows (its slug), without account.</summary>
    internal static IReadOnlyList<string> ParseGameSearch(string json) =>
        (Data(json, EaJsonContext.Default.GraphQlEnvelopeGameSearchData).GameSearch?.Items
            ?? throw new EaException(CoreStrings.Ea_Format))
        .Select(i => i.Slug)
        .OfType<string>()
        .Distinct()
        .ToList();

    /// <summary>The games of the given slugs the player played: play time and last session.</summary>
    internal static IReadOnlyList<EaPlayedGame> ParseRecentGames(string json) =>
        (Data(json, EaJsonContext.Default.GraphQlEnvelopeMeData).Me?.RecentGames?.Items
            ?? throw new EaException(CoreStrings.Ea_Format))
        .Where(g => g.GameSlug is { Length: > 0 })
        .Select(g => new EaPlayedGame(
            g.GameSlug!,
            g.TotalPlayTimeSeconds is long seconds ? (int)(seconds / 60) : null,
            LastPlayedOf(g.LastSessionEndDate)))
        .ToList();

    // "1970-01-01T00:00:00.000Z": EA does not know the date.
    private static DateTimeOffset? LastPlayedOf(string? date) =>
        DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) && parsed.Year > 1970
            ? parsed
            : null;

    /// <summary>
    /// Each slug's game: the name and offer of its first base game (an
    /// edition, a region: every offer of a game shares its achievements).
    /// </summary>
    internal static IReadOnlyList<EaGameProduct> ParseGames(string json) =>
        (Data(json, EaJsonContext.Default.GraphQlEnvelopeGamesData).Games?.Items
            ?? throw new EaException(CoreStrings.Ea_Format))
        .Select(game => (game.Slug, Product: game.Products?.Items?.FirstOrDefault(p =>
            p.BaseItem?.GameType == "BASE_GAME" && p.Name is { Length: > 0 } && p.OriginOfferId is { Length: > 0 })))
        .Where(g => g.Slug is not null && g.Product is not null)
        .Select(g => new EaGameProduct(g.Slug!, g.Product!.Name!, g.Product.OriginOfferId!))
        .ToList();

    /// <summary>Unlocked and total achievements of a game, or null when it has none.</summary>
    internal static EaAchievements? ParseAchievements(string json)
    {
        var sets = Data(json, EaJsonContext.Default.GraphQlEnvelopeAchievementsData).Achievements
            ?? throw new EaException(CoreStrings.Ea_Format);
        // One set per game in practice; the largest if a game ever has several.
        var achievements = sets.Select(s => s.Achievements ?? []).MaxBy(a => a.Count);
        return achievements is { Count: > 0 }
            ? new EaAchievements(achievements.Count(a => a.AwardCount > 0), achievements.Count)
            : null;
    }

    /// <summary>The data of a GraphQL answer; none (errors, a maintenance page) throws <see cref="EaException"/>.</summary>
    private static TData Data<TData>(string json, JsonTypeInfo<GraphQlEnvelope<TData>> typeInfo) where TData : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo)?.Data ?? throw new EaException(CoreStrings.Ea_Format);
        }
        catch (JsonException ex)
        {
            throw new EaException(CoreStrings.Ea_Format, ex);
        }
    }
}

/// <summary>A game the player played in the EA app (play time null when EA does not say).</summary>
internal sealed record EaPlayedGame(string Slug, int? PlaytimeMinutes, DateTimeOffset? LastPlayed);

/// <summary>A game's display name and the offer its achievements are asked for.</summary>
internal sealed record EaGameProduct(string Slug, string Name, string OfferId);

/// <summary>Achievements of one game.</summary>
internal sealed record EaAchievements(int Unlocked, int Total);
