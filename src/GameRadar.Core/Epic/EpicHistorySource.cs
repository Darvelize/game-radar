using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.Epic;

/// <summary>
/// Gives the Epic history: the library and the play time (raw answers, cached
/// as-is and read again at each load), the title of each game played (compact
/// cache, final: a title does not change) and its achievements (compact cache,
/// asked again only for a game played since). Epic is only called when the
/// user asks for it.
/// </summary>
public sealed class EpicHistorySource
{
    private const string HistoryKey = "epic-history";
    private const string TitlesKey = "epic-titles";
    private const string AchievementsKey = "epic-achievements";

    // A library is a few pages of 100 to 200 entries (4 pages for 533): a
    // cursor that never ends is an answer gone wrong, not a library.
    private const int MaxLibraryPages = 100;

    private readonly CacheStore _cache;

    public EpicHistorySource(CacheStore cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <summary>
    /// Returns the last saved history, or null when there is none.
    /// No network call. Never throws: a corrupted cache counts as missing.
    /// </summary>
    public GameHistory? LoadCached()
    {
        if (_cache.TryLoad(HistoryKey) is not { } entry)
            return null;

        try
        {
            var (library, playtime) = ParseHistory(entry.Content);
            return new GameHistory(ToPlayedGames(library, playtime, LoadTitles(), LoadAchievements()), entry.SavedAt);
        }
        catch (Exception ex) when (ex is JsonException or EpicException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the library and the play time (~5 requests), the title of each game
    /// played not known yet and the achievements of the games played since, then
    /// replaces the cache. Throws <see cref="EpicException"/> (session refused,
    /// rate limit...): the old history is kept.
    /// </summary>
    public async Task<GameHistory> RefreshAsync(EpicClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var pages = new List<string>();
        var library = new List<EpicLibraryEntry>();
        string? cursor = null;
        do
        {
            string json = await client.FetchLibraryPageJsonAsync(cursor, ct).ConfigureAwait(false);
            // Parsed BEFORE saving: an unexpected answer throws here and the
            // previous, valid cache is kept.
            var page = EpicClient.ParseLibrary(json);
            pages.Add(json);
            library.AddRange(page.Entries);
            cursor = page.NextCursor;
        }
        while (cursor is not null && pages.Count < MaxLibraryPages);
        // Never saved cut short: the games of the later pages would vanish from the history.
        if (cursor is not null)
            throw new EpicException(CoreStrings.Epic_Format);

        string playtimeJson = await client.FetchPlaytimeJsonAsync(ct).ConfigureAwait(false);
        var playtime = EpicClient.ParsePlaytime(playtimeJson);

        var titles = LoadTitles();
        var achievements = LoadAchievements();
        await FetchDetailsAsync(client, Played(library, playtime), titles, achievements, ct).ConfigureAwait(false);

        await _cache.SaveAsync(HistoryKey,
            $$"""{"library":[{{string.Join(",", pages)}}],"playtime":{{playtimeJson}}}""", ct).ConfigureAwait(false);
        return new GameHistory(ToPlayedGames(library, playtime, titles, achievements), DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The title of each game played not known yet, then the achievements of
    /// the games whose play time grew since they were read, one request each,
    /// most played first. One game failing (an odd or refused answer) is asked
    /// again next time; the network failing, a refused session or the rate limit
    /// stop the loop. What was read is saved whatever happens.
    /// </summary>
    private async Task FetchDetailsAsync(
        EpicClient client, IReadOnlyList<(EpicLibraryEntry Entry, int Minutes)> played,
        Dictionary<string, EpicTitle> titles, Dictionary<string, AchievementEntry> achievements, CancellationToken ct)
    {
        var newTitles = new Dictionary<string, EpicTitle>();
        var newAchievements = new Dictionary<string, AchievementEntry>();
        try
        {
            foreach (var (entry, minutes) in played)
            {
                try
                {
                    if (!titles.TryGetValue(entry.CatalogItemId, out var title))
                    {
                        // Unknown to the catalog: asked again next time (rare).
                        if (await client.FetchCatalogItemAsync(entry.Namespace, entry.CatalogItemId, ct).ConfigureAwait(false) is not { } found)
                            continue;
                        title = titles[entry.CatalogItemId] = newTitles[entry.CatalogItemId] = found;
                    }
                    if (!title.IsGame || achievements.GetValueOrDefault(entry.AppName) is { } known && known.Minutes >= minutes)
                        continue;

                    int? total = await client.FetchAchievementsTotalAsync(entry.Namespace, ct).ConfigureAwait(false);
                    int unlocked = total is > 0 ? await client.FetchAchievementsUnlockedAsync(entry.Namespace, ct).ConfigureAwait(false) : 0;
                    achievements[entry.AppName] = newAchievements[entry.AppName] = new AchievementEntry(unlocked, total ?? 0, minutes);
                }
                catch (EpicException ex) when (ex is { IsTokenRefused: false, IsRateLimited: false })
                {
                    // This game's answer only (odd, refused, a 5xx): the next game is asked.
                }
                catch (Exception ex) when (StoreLookup.Transient(ex, ct))
                {
                    // Unreachable or too slow: the next requests would wait as long
                    // (30 s each); the rest is asked at the next sync.
                    break;
                }
            }
        }
        finally
        {
            await _cache.MergeDictionaryAsync(TitlesKey, newTitles, EpicJsonContext.Default.DictionaryStringEpicTitle).ConfigureAwait(false);
            await _cache.MergeDictionaryAsync(AchievementsKey, newAchievements, EpicJsonContext.Default.DictionaryStringAchievementEntry).ConfigureAwait(false);
        }
    }

    /// <summary>The library entries of the games launched, with their play time, most played first.</summary>
    private static List<(EpicLibraryEntry Entry, int Minutes)> Played(IEnumerable<EpicLibraryEntry> library, IEnumerable<EpicPlaytime> playtime)
    {
        // An app can be in the library twice (two grants of one game): one is enough.
        return playtime
            .Join(library.DistinctBy(e => e.AppName), p => p.AppName, e => e.AppName, (p, e) => (Entry: e, p.Minutes))
            .OrderByDescending(p => p.Minutes)
            .ToList();
    }

    /// <summary>The cached raw answers → the library entries and the play time.</summary>
    private static (List<EpicLibraryEntry> Library, IReadOnlyList<EpicPlaytime> Playtime) ParseHistory(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (
            root.GetProperty("library").EnumerateArray().SelectMany(page => EpicClient.ParseLibrary(page.GetRawText()).Entries).ToList(),
            EpicClient.ParsePlaytime(root.GetProperty("playtime").GetRawText()));
    }

    /// <summary>
    /// The games played → history entries, most played first. Only games: an
    /// add-on (a DLC repeats its game's play time) or an engine is left out, and
    /// so is a game whose title is not known yet. Epic gives no last-played date.
    /// </summary>
    internal static IReadOnlyList<PlayedGame> ToPlayedGames(
        IEnumerable<EpicLibraryEntry> library,
        IEnumerable<EpicPlaytime> playtime,
        IReadOnlyDictionary<string, EpicTitle> titles,
        IReadOnlyDictionary<string, AchievementEntry> achievements) =>
        Played(library, playtime)
            .Where(p => titles.GetValueOrDefault(p.Entry.CatalogItemId) is { IsGame: true })
            .Select(p =>
            {
                // Total 0: the game has none.
                var (unlocked, total) = achievements.GetValueOrDefault(p.Entry.AppName) is { Total: > 0 } entry
                    ? (entry.Unlocked, (int?)entry.Total) : (0, null);
                double percentage = total is { } count ? 100.0 * unlocked / count : 0;

                return new PlayedGame(
                    Platform: EpicClient.PlatformName,
                    PlatformId: p.Entry.AppName,
                    Name: titles[p.Entry.CatalogItemId].Title,
                    HasAchievements: total is not null,
                    UnlockedAchievements: unlocked,
                    TotalAchievements: total,
                    ProgressPercentage: percentage,
                    LastPlayed: null,
                    Status: StatusClassifier.FromAchievements(unlocked, total, percentage),
                    PlaytimeMinutes: p.Minutes);
            })
            .ToList();

    private Dictionary<string, EpicTitle> LoadTitles() =>
        _cache.LoadDictionary(TitlesKey, EpicJsonContext.Default.DictionaryStringEpicTitle);

    private Dictionary<string, AchievementEntry> LoadAchievements() =>
        _cache.LoadDictionary(AchievementsKey, EpicJsonContext.Default.DictionaryStringAchievementEntry);

    /// <summary>Achievements of one game at a given play time. Total 0 = the game has none.</summary>
    internal sealed record AchievementEntry(int Unlocked, int Total, int Minutes);
}
