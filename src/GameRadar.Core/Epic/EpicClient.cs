using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using GameRadar.Core.Services;

namespace GameRadar.Core.Epic;

/// <summary>
/// The games played on Epic Games, their play time and achievements, through
/// the services the Epic launcher uses (unofficial), with the launcher's own
/// client (Epic registers no
/// third-party app).
/// </summary>
public sealed partial class EpicClient
{
    /// <summary>The platform name of Epic games (a brand, never translated).</summary>
    public const string PlatformName = "Epic";

    /// <summary>
    /// The Epic launcher's client, public in Legendary (the open-source
    /// launcher Heroic and Playnite build on).
    /// </summary>
    internal const string ClientId = "34a02cf8f4414e29b15921876da36f9a";
    internal const string ClientSecret = "daafbccc737745039dffe53d94fc76cf";

    /// <summary>
    /// The page the user opens in their browser, signed in to epicgames.com: it
    /// shows the JSON with the authorization code GameRadar exchanges once.
    /// </summary>
    public const string CodePageUrl = "https://www.epicgames.com/id/api/redirect?clientId=" + ClientId + "&responseType=code";

    // The launcher's identity: for the account and library services.
    internal const string LauncherUserAgent = "UELauncher/11.0.1-14907503+++Portal+Release-Live Windows/10.0.19041.1.256.64bit";

    // The store's GraphQL answers 403 to anything but the launcher (measured).
    internal const string StoreUserAgent = "EpicGamesLauncher/14.0.8-22004686+++Portal+Release-Live";

    private const string LibraryUrl = "https://library-service.live.use1a.on.epicgames.com/library/api/public/items?includeMetadata=true";
    private const string PlaytimeUrl = "https://library-service.live.use1a.on.epicgames.com/library/api/public/playtime/account/{0}/all";
    private const string CatalogItemUrl = "https://catalog-public-service-prod06.ol.epicgames.com/catalog/api/shared/namespace/{0}/bulk/items?id={1}&country=FR&locale=en-US";
    // The store's GraphQL: achievements with the account, the catalog without.
    internal const string GraphQlUrl = "https://launcher.store.epicgames.com/graphql";

    private const string AchievementsTotalQuery = "query Achievement($sandboxId: String!, $locale: String!) { Achievement { productAchievementsRecordBySandbox(sandboxId: $sandboxId, locale: $locale) { totalAchievements } } }";
    private const string AchievementsUnlockedQuery = "query PlayerAchievement($epicAccountId: String!, $sandboxId: String!) { PlayerAchievement { playerAchievementGameRecordsBySandbox(epicAccountId: $epicAccountId, sandboxId: $sandboxId) { records { totalUnlocked } } } }";

    private readonly HttpClient _http;
    private readonly EpicAccount _account;

    public EpicClient(HttpClient http, EpicAccount account)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(account);
        _http = http;
        _account = account;
    }

    /// <summary>The raw answer of one library page (null cursor: the first), read with <see cref="ParseLibrary"/> (kept raw in the cache).</summary>
    internal Task<string> FetchLibraryPageJsonAsync(string? cursor, CancellationToken ct = default) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, cursor is null ? LibraryUrl : LibraryUrl + "&cursor=" + Uri.EscapeDataString(cursor)),
            LauncherUserAgent, ct);

    /// <summary>The raw play time of every game launched, read with <see cref="ParsePlaytime"/> (kept raw in the cache).</summary>
    internal Task<string> FetchPlaytimeJsonAsync(CancellationToken ct = default) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, string.Format(PlaytimeUrl, Uri.EscapeDataString(AccountId))),
            LauncherUserAgent, ct);

    /// <summary>A catalog item's title and whether it is a game; null when the catalog does not know it.</summary>
    internal async Task<EpicTitle?> FetchCatalogItemAsync(string @namespace, string catalogItemId, CancellationToken ct = default) =>
        ParseCatalogItem(await SendAsync(() => new HttpRequestMessage(HttpMethod.Get,
            string.Format(CatalogItemUrl, Uri.EscapeDataString(@namespace), Uri.EscapeDataString(catalogItemId))),
            LauncherUserAgent, ct).ConfigureAwait(false), catalogItemId);

    /// <summary>A game's number of achievements (its namespace is its sandbox), or null when it has none.</summary>
    internal async Task<int?> FetchAchievementsTotalAsync(string @namespace, CancellationToken ct = default) =>
        ParseAchievementsTotal(await PostGraphQlAsync(
            new GraphQlRequest<SandboxVariables> { Query = AchievementsTotalQuery, Variables = new() { SandboxId = @namespace } },
            EpicJsonContext.Default.GraphQlRequestSandboxVariables, ct).ConfigureAwait(false));

    /// <summary>The achievements the player unlocked in a game.</summary>
    internal async Task<int> FetchAchievementsUnlockedAsync(string @namespace, CancellationToken ct = default) =>
        ParseAchievementsUnlocked(await PostGraphQlAsync(
            new GraphQlRequest<PlayerSandboxVariables> { Query = AchievementsUnlockedQuery, Variables = new() { EpicAccountId = AccountId, SandboxId = @namespace } },
            EpicJsonContext.Default.GraphQlRequestPlayerSandboxVariables, ct).ConfigureAwait(false));

    private string AccountId => _account.AccountId ?? throw EpicException.SessionExpired();

    private Task<string> PostGraphQlAsync<T>(T body, JsonTypeInfo<T> typeInfo, CancellationToken ct) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Post, GraphQlUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, typeInfo), System.Text.Encoding.UTF8, "application/json")
        }, StoreUserAgent, ct);

    /// <summary>
    /// Sends with the access token. Refused (401), the token is renewed once and
    /// the request sent again: it may have been revoked or expired early.
    /// </summary>
    private async Task<string> SendAsync(Func<HttpRequestMessage> build, string userAgent, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            string token = await _account.GetAccessTokenAsync(ct).ConfigureAwait(false);
            using var request = build();
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd(userAgent);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            switch (HttpOutcomes.Classify(response))
            {
                case HttpOutcome.Ok:
                    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                case HttpOutcome.Unauthorized when attempt == 0:
                    _account.ForgetAccessToken(token);
                    continue;
                case HttpOutcome.Unauthorized:
                    // Refused again with a token just renewed: the session is gone
                    // (revoked); dropped, so the card asks for a new code.
                    _account.Disconnect();
                    throw EpicException.SessionExpired();
                case HttpOutcome.RateLimited:
                    throw EpicException.RateLimited();
                default:
                    throw new EpicException(CoreStrings.Epic_Down);
            }
        }
    }

    /// <summary>
    /// The code in what the user pasted: the whole JSON of the code page, or
    /// the code alone. Throws <see cref="EpicException"/> saying what to do.
    /// </summary>
    public static string RequireCode(string pasted)
    {
        string text = pasted.Trim();
        if (text.StartsWith('{'))
        {
            CodePageDto? page;
            try
            {
                page = JsonSerializer.Deserialize(text, EpicJsonContext.Default.CodePageDto);
            }
            catch (JsonException)
            {
                throw new EpicException(CoreStrings.Epic_PasteCode);
            }
            // The page of a browser signed out of epicgames.com has no code.
            text = page?.AuthorizationCode ?? throw new EpicException(CoreStrings.Epic_SignInFirst);
        }
        return CodePattern().IsMatch(text) ? text : throw new EpicException(CoreStrings.Epic_PasteCode);
    }

    // Epic's authorization codes: 32 hex characters (measured).
    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex CodePattern();

    /// <summary>A code exchanged or a session renewed: the tokens, the access one valid until <c>now + expires_in</c>.</summary>
    internal static EpicTokens ParseTokens(string json, DateTimeOffset now)
    {
        var dto = Read(json, EpicJsonContext.Default.TokensDto);
        return dto is { AccessToken.Length: > 0, ExpiresIn: int seconds, RefreshToken.Length: > 0, AccountId.Length: > 0 }
            ? new EpicTokens(dto.AccessToken, now.AddSeconds(seconds), dto.RefreshToken, dto.AccountId)
            : throw new EpicException(CoreStrings.Epic_Format);
    }

    /// <summary>One page of the library: its entries (those with a catalog item) and the cursor of the next one.</summary>
    internal static EpicLibraryPage ParseLibrary(string json)
    {
        var dto = Read(json, EpicJsonContext.Default.LibraryPageDto);
        var entries = (dto?.Records ?? throw new EpicException(CoreStrings.Epic_Format))
            .Select(r => r is { AppName: { Length: > 0 } app, Namespace: { Length: > 0 } ns, CatalogItemId: { Length: > 0 } item }
                ? new EpicLibraryEntry(app, ns, item) : null)
            .OfType<EpicLibraryEntry>()
            .ToList();
        return new EpicLibraryPage(entries, dto.ResponseMetadata?.NextCursor);
    }

    /// <summary>The play time of every game the player launched, in minutes.</summary>
    internal static IReadOnlyList<EpicPlaytime> ParsePlaytime(string json) =>
        (Read(json, EpicJsonContext.Default.ListPlaytimeDto) ?? throw new EpicException(CoreStrings.Epic_Format))
        .Select(p => p is { ArtifactId: { Length: > 0 } app, TotalTime: long seconds } ? new EpicPlaytime(app, (int)(seconds / 60)) : null)
        .OfType<EpicPlaytime>()
        .ToList();

    /// <summary>
    /// A catalog item's title, and whether it is a game: "games" but neither an
    /// add-on (a DLC, which repeats its game's play time) nor an engine (Unreal).
    /// Null when the catalog does not know it.
    /// </summary>
    internal static EpicTitle? ParseCatalogItem(string json, string catalogItemId)
    {
        var items = Read(json, EpicJsonContext.Default.DictionaryStringCatalogItemDto) ?? throw new EpicException(CoreStrings.Epic_Format);
        if (!items.TryGetValue(catalogItemId, out var item) || item.Title is not { Length: > 0 } title)
            return null;
        var paths = (item.Categories ?? []).Select(c => c.Path).OfType<string>().ToList();
        bool isGame = paths.Contains("games") && !paths.Any(p => p == "addons" || p.StartsWith("engines", StringComparison.Ordinal));
        return new EpicTitle(title.Trim(), isGame);
    }

    /// <summary>A game's number of achievements, or null when it has none.</summary>
    internal static int? ParseAchievementsTotal(string json) =>
        Data(json, EpicJsonContext.Default.GraphQlEnvelopeAchievementData).Achievement?.Product?.TotalAchievements;

    /// <summary>The achievements the player unlocked in a game (no record: none).</summary>
    internal static int ParseAchievementsUnlocked(string json) =>
        Data(json, EpicJsonContext.Default.GraphQlEnvelopePlayerAchievementData).PlayerAchievement?.GameRecords?.Records?
            .FirstOrDefault()?.TotalUnlocked ?? 0;

    /// <summary>The data of a GraphQL answer; none (errors, a maintenance page) throws <see cref="EpicException"/>.</summary>
    /// <remarks>
    /// Any error makes the whole answer unreadable, even with partial data: a
    /// field left null by a failing resolver would read as "no achievements"
    /// and be kept until the game is played again.
    /// </remarks>
    internal static TData Data<TData>(string json, JsonTypeInfo<GraphQlEnvelope<TData>> typeInfo) where TData : class =>
        Read(json, typeInfo) is { Errors: null or { Count: 0 }, Data: { } data } ? data : throw new EpicException(CoreStrings.Epic_Format);

    /// <summary>
    /// A token endpoint refusal that means the code or the refresh token is no
    /// good (used, expired, revoked), not a firewall, terms to accept or a
    /// client Epic no longer takes.
    /// </summary>
    internal static bool IsGrantRefused(string json)
    {
        string? code;
        try
        {
            code = JsonSerializer.Deserialize(json, EpicJsonContext.Default.TokensDto)?.ErrorCode;
        }
        catch (JsonException)
        {
            return false;
        }
        // errors.com.epicgames.account.oauth.authorization_code_not_found,
        // ….auth_token.invalid_refresh_token, ….oauth.invalid_grant
        return code is not null && (code.EndsWith("_not_found", StringComparison.Ordinal)
            || code.Contains("invalid_refresh_token", StringComparison.Ordinal)
            || code.Contains("invalid_grant", StringComparison.Ordinal));
    }

    internal static T? Read<T>(string json, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException ex)
        {
            throw new EpicException(CoreStrings.Epic_Format, ex);
        }
    }
}

/// <summary>A session: the access token (memory only) and the refresh token (kept, replaced at each use).</summary>
internal sealed record EpicTokens(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken, string AccountId);

/// <summary>A library entry: what its play time, catalog item and achievements are asked by.</summary>
internal sealed record EpicLibraryEntry(string AppName, string Namespace, string CatalogItemId);

/// <summary>One page of the library; the last has no cursor.</summary>
internal sealed record EpicLibraryPage(IReadOnlyList<EpicLibraryEntry> Entries, string? NextCursor);

/// <summary>The play time of a game the player launched.</summary>
internal sealed record EpicPlaytime(string AppName, int Minutes);

/// <summary>A catalog item's title, and whether it is a game.</summary>
internal sealed record EpicTitle(string Title, bool IsGame);
