using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace GameRadar.Core.PlayStation;

/// <summary>A game with trophies: one trophy set, shared by the PS4/PS5 versions of a game.</summary>
internal sealed record PlayStationTrophyTitle(
    string CommunicationId,
    string Name,
    double Progress,
    int Earned,
    int Defined,
    bool HasPlatinum,
    DateTimeOffset? LastUpdated);

/// <summary>A PS4/PS5 game from the list of played games, with its play time and its genres (Store vocabulary).</summary>
internal sealed record PlayStationGame(
    string TitleId, string Name, int PlaytimeMinutes, DateTimeOffset? LastPlayed, IReadOnlyList<string> Genres);

/// <summary>
/// Connector for PlayStation Network (APIs of the PlayStation App), called
/// with the user's own session (<see cref="PlayStationAccount"/>).
/// Its only job: call the API and translate the answers.
/// </summary>
public sealed partial class PlayStationClient
{
    /// <summary>Value of <see cref="Models.PlayedGame.Platform"/> for games from this connector.</summary>
    public const string PlatformName = "PlayStation";

    private const string TrophyTitlesUrl = "https://m.np.playstation.com/api/trophy/v1/users/me/trophyTitles?limit={0}&offset={1}";
    // The categories of games (not of apps); "unknown" holds real games too
    // (free-to-play, PS4 games started on a PS5...).
    private const string GamesUrl = "https://m.np.playstation.com/api/gamelist/v2/users/me/titles?categories=ps4_game,ps5_native_game,pspc_game,unknown&limit={0}&offset={1}";
    private const string TrophySetsUrl = "https://m.np.playstation.com/api/trophy/v1/users/me/titles/trophyTitles?npTitleIds={0}";

    private const int TrophyTitlesPerPage = 800;
    private const int GamesPerPage = 200;
    /// <summary>The API's limit for <see cref="FetchTrophySetsAsync"/>.</summary>
    internal const int TitleIdsPerRequest = 5;


    private readonly HttpClient _http;
    private readonly PlayStationAccount _account;

    public PlayStationClient(HttpClient http, PlayStationAccount account)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(account);
        _http = http;
        _account = account;
    }

    /// <summary>Every game with trophies, as raw pages (1 request per 800 games).</summary>
    internal Task<IReadOnlyList<string>> FetchTrophyTitlePagesAsync(CancellationToken ct) =>
        FetchPagesAsync(TrophyTitlesUrl, TrophyTitlesPerPage, json =>
        {
            var page = ParseTrophyTitlesPage(json);
            return (page.TrophyTitles!.Count, page.TotalItemCount);
        }, ct);

    /// <summary>Every PS4/PS5 game played, as raw pages (1 request per 200 games).</summary>
    internal Task<IReadOnlyList<string>> FetchGamePagesAsync(CancellationToken ct) =>
        FetchPagesAsync(GamesUrl, GamesPerPage, json =>
        {
            var page = ParseGamesPage(json);
            return (page.Titles!.Count, page.TotalItemCount);
        }, ct);

    /// <summary>
    /// The trophy sets of played games (title id → set ids, empty for a game
    /// without trophies), 1 request per <see cref="TitleIdsPerRequest"/> games:
    /// the exact link between both lists, whose names differ ("Trophées Mortal
    /// Kombat X", "Lords of the Fallen (2014)").
    /// </summary>
    internal async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FetchTrophySetsAsync(
        IReadOnlyList<string> titleIds, CancellationToken ct)
    {
        var sets = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var batch in titleIds.Chunk(TitleIdsPerRequest))
        {
            string json = await GetAsync(string.Format(CultureInfo.InvariantCulture, TrophySetsUrl,
                string.Join(",", batch.Select(Uri.EscapeDataString))), ct).ConfigureAwait(false);
            foreach (var (titleId, setIds) in ParseTrophySets(json))
                sets[titleId] = setIds;
        }
        return sets;
    }

    // ---------- Parsing, separate from HTTP (testable without network) ----------

    internal static IReadOnlyList<PlayStationTrophyTitle> ParseTrophyTitles(TrophyTitlesPageDto page) =>
        (page.TrophyTitles ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.NpCommunicationId) && !string.IsNullOrWhiteSpace(t.TrophyTitleName))
            .Select(t => new PlayStationTrophyTitle(
                t.NpCommunicationId!,
                CleanTrophyTitleName(t.TrophyTitleName!),
                t.Progress,
                t.EarnedTrophies?.Total ?? 0,
                t.DefinedTrophies?.Total ?? 0,
                t.EarnedTrophies?.Platinum > 0,
                t.LastUpdatedDateTime))
            .ToList();

    internal static IReadOnlyList<PlayStationGame> ParseGames(GamesPageDto page) =>
        (page.Titles ?? [])
            // An empty localized name gives way to the name (not only a null one).
            .Select(t => (Dto: t, Name: string.IsNullOrWhiteSpace(t.LocalizedName) ? t.Name : t.LocalizedName))
            .Where(t => !string.IsNullOrWhiteSpace(t.Dto.TitleId) && !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => new PlayStationGame(
                t.Dto.TitleId!,
                t.Name!.Trim(),
                PlaytimeMinutesOf(t.Dto.PlayDuration),
                t.Dto.LastPlayedDateTime,
                (t.Dto.Concept?.Genres ?? []).Select(GenreOf).OfType<string>().Distinct().Take(MaxGenres).ToList()))
            .ToList();

    // As many as the Steam store candidates keep (SteamCatalogClient).
    internal const int MaxGenres = 3;

    /// <summary>
    /// PlayStation genre → Microsoft Store genre (the vocabulary of the whole
    /// engine), or null when it says nothing of the kind of game ("UNIQUE",
    /// "ARCADE", "CASUAL"). Coarse: the last resort for a game found neither
    /// on Steam (its tags) nor in the Microsoft Store (its genres).
    /// </summary>
    internal static string? GenreOf(string genre) => genre switch
    {
        "ACTION" or "ADVENTURE" or "ACTION_ADVENTURE" or "HORROR" or "SURVIVAL" => "Action & adventure",
        "ROLE_PLAYING_GAMES" or "RPG" => "Role playing",
        "SHOOTER" => "Shooter",
        "FIGHTING" => "Fighting",
        "SPORTS" or "SPORT" => "Sports",
        "RACING" or "DRIVING_RACING" or "DRIVING/RACING" => "Racing & flying",
        "STRATEGY" => "Strategy",
        "SIMULATION" or "SIMULATOR" => "Simulation",
        "PUZZLE" or "QUIZ" or "BRAIN_TRAINING" => "Puzzle & trivia",
        "PLATFORMER" => "Platformer",
        "MUSIC_RHYTHM" or "MUSIC/RHYTHM" or "MUSIC" => "Music",
        "FAMILY" or "PARTY" => "Family & kids",
        "EDUCATIONAL" => "Educational",
        _ => null
    };

    /// <summary>
    /// Minutes in an ISO 8601 duration ("PT159H21M30S"). At least 1: the
    /// list only holds games that were launched, and 0 would mean "owned,
    /// never played" (<see cref="Models.PlayedGame.NeverPlayed"/>).
    /// </summary>
    internal static int PlaytimeMinutesOf(string? duration)
    {
        double minutes = 0;
        if (!string.IsNullOrEmpty(duration))
        {
            try
            {
                minutes = XmlConvert.ToTimeSpan(duration).TotalMinutes;
            }
            catch (FormatException)
            {
            }
        }
        return Math.Max(1, (int)minutes);
    }

    /// <summary>
    /// The game's name in a trophy set's name: some sets are called
    /// "Trophées X" or "Collection de trophées de X" (in English "X Trophies").
    /// </summary>
    internal static string CleanTrophyTitleName(string name)
    {
        string cleaned = TrophyWordsPattern().Replace(name.Trim(), "").Trim();
        return cleaned.Length > 0 ? cleaned : name.Trim();
    }

    internal static TrophyTitlesPageDto ParseTrophyTitlesPage(string json) =>
        JsonSerializer.Deserialize(json, PlayStationJsonContext.Default.TrophyTitlesPageDto) is { TrophyTitles: not null } page
            ? page
            : throw new PlayStationException(CoreStrings.PlayStation_Format);

    internal static GamesPageDto ParseGamesPage(string json) =>
        JsonSerializer.Deserialize(json, PlayStationJsonContext.Default.GamesPageDto) is { Titles: not null } page
            ? page
            : throw new PlayStationException(CoreStrings.PlayStation_Format);

    internal static IEnumerable<(string TitleId, IReadOnlyList<string> SetIds)> ParseTrophySets(string json)
    {
        var answer = JsonSerializer.Deserialize(json, PlayStationJsonContext.Default.TitleTrophySetsDto);
        if (answer?.Titles is null)
            throw new PlayStationException(CoreStrings.PlayStation_Format);

        return answer.Titles
            .Where(t => !string.IsNullOrWhiteSpace(t.NpTitleId))
            .Select(t => (t.NpTitleId!, (IReadOnlyList<string>)(t.TrophyTitles ?? [])
                .Select(s => s.NpCommunicationId)
                .OfType<string>()
                .ToList()));
    }

    // ---------- HTTP ----------

    private async Task<IReadOnlyList<string>> FetchPagesAsync(
        string urlFormat, int pageSize, Func<string, (int Count, int Total)> countOf, CancellationToken ct)
    {
        var pages = new List<string>();
        int offset = 0;
        while (true)
        {
            string json = await GetAsync(string.Format(CultureInfo.InvariantCulture, urlFormat, pageSize, offset), ct)
                .ConfigureAwait(false);
            // Parsed here too: an unexpected answer throws before anything is cached.
            var (count, total) = countOf(json);
            pages.Add(json);
            offset += count;
            // Without a total (0: left out), read until an empty page rather
            // than stop after the first one and cut the history silently.
            if (count == 0 || (total > 0 && offset >= total))
                return pages;
        }
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        // One retry with a new token: the one in memory may have been revoked.
        for (int attempt = 1; ; attempt++)
        {
            string token = await _account.GetAccessTokenAsync(ct).ConfigureAwait(false);

            // Headers are set on the request, not on the shared HttpClient.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // Game names in English, like every other source: a game is matched
            // across platforms (and with Steam) by its title.
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
            {
                _account.ForgetAccessToken(token);
                continue;
            }

            EnsureSuccess(response);
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        throw response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests =>
                PlayStationException.RateLimited(),
            _ =>
                new PlayStationException(CoreText.Format(CoreStrings.PlayStation_HttpError, (int)response.StatusCode, response.ReasonPhrase))
        };
    }

    [GeneratedRegex(@"^(Collection de trophées de|Trophées)\s+|\s+(Trophies|Trophy Set)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrophyWordsPattern();
}
