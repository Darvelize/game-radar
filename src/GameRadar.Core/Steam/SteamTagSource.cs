using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Xbox;

namespace GameRadar.Core.Steam;

/// <summary>
/// Finds the Steam tags of the games the taste profile and the similarity
/// search use: every played game that counts in the taste, whatever its
/// platform, and the Game Pass candidates (the Steam store candidates carry
/// theirs already). By app id for Steam games, by title for the others. Cached for good (tags hardly move); games not found on
/// Steam are retried after 30 days. The first run costs ~1 request per Game
/// Pass game (~2 min, two at a time), later runs only the new games.
/// </summary>
public sealed class SteamTagSource
{
    internal static readonly TimeSpan RetryNotFoundAfter = TimeSpan.FromDays(30);
    private static readonly TimeSpan TagNamesMaxAge = TimeSpan.FromDays(30);

    /// <summary>
    /// Title searches at once. Measured (14/10/2026, SearchSuggestions, 240 requests, no
    /// rate limit): one at a time with a 250 ms pause 547 ms per title (812 Game Pass
    /// titles: ~7 min), without the pause 258 ms, 2 at a time 145 ms (~2 min), 4 at a time
    /// 132 ms, 8 at a time 146 ms. A rate limit stops the lookup; the rest comes next time.
    /// </summary>
    internal const int TitleSearchesAtOnce = 2;

    /// <summary>After a rate limit, Steam is asked nothing for this long (the cached tags still recommend).</summary>
    internal static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(5);

    // Saved along the way: closing the app during the first run keeps what was found.
    private const int SaveEvery = 50;

    internal const string CacheKey = "steam-tags-v1";
    // One cache per language; French keeps its first key, so nothing is
    // fetched again for those who stay in French.
    private static string TagNamesCacheKey(string steamLanguage) =>
        steamLanguage == "french" ? "steam-tag-names-fr" : "steam-tag-names-" + steamLanguage;


    private readonly CacheStore _cache;
    // Loaded once, shared by every computation; "lock (_entries)" around each
    // access, since a lookup may run while another computation reads.
    private Dictionary<string, TagEntry>? _entries;

    // One lookup run at a time: a new computation waits for the cancelled
    // one to stop, then goes on where it stopped.
    private readonly SemaphoreSlim _fetchLock = new(1, 1);

    private readonly TimeProvider _time;
    // Set by a rate limit: no request to Steam before then (UTC ticks, read from any thread).
    private long _pausedUntilTicks;

    private bool IsPaused => _time.GetUtcNow().UtcTicks < Interlocked.Read(ref _pausedUntilTicks);

    // After a rate limit, no request to Steam for a while: asked again at once, it would only refuse again.
    private void PauseIfRateLimited(Exception ex)
    {
        if (ex is ServiceException { IsRateLimited: true })
            Interlocked.Exchange(ref _pausedUntilTicks, (_time.GetUtcNow() + RateLimitPause).UtcTicks);
    }

    // Set when the tag names could not be read (Steam down, no network): not asked again
    // before then, or every computation would wait for the same failure again.
    private long _namesRetryAfterTicks;

    /// <param name="time">The clock (tests move their own). Null = the system's.</param>
    public SteamTagSource(CacheStore cache, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>What is known now (cache + Steam store candidates), without any request.</summary>
    /// <param name="addedTags">The tags the player gave their games (<see cref="Settings.AppSettings.AddedTags"/>), copied.</param>
    public SteamTagIndex LoadIndex(IEnumerable<CatalogGame> candidates, IReadOnlyDictionary<string, List<int>>? addedTags = null)
    {
        var entries = Entries();
        Dictionary<string, TagEntry> snapshot;
        lock (entries)
            snapshot = new Dictionary<string, TagEntry>(entries);
        return new SteamTagIndex(snapshot, candidates, addedTags);
    }

    /// <summary>
    /// Games whose tags are still unknown and worth a request: played games
    /// that count in the taste (<paramref name="countsInTaste"/>) and Game Pass candidates.
    /// What a lookup would ask, without asking: for the tests (the app looks up at once).
    /// </summary>
    internal int CountMissing(SteamTagIndex index, IEnumerable<PlayedGame> played, IEnumerable<CatalogGame> candidates, DateTimeOffset now,
        Func<PlayedGame, bool> countsInTaste)
    {
        var (appIds, titles, _, _) = Missing(index, played, candidates, now, countsInTaste);
        return appIds.Count + titles.Count;
    }

    /// <summary>
    /// Looks up the missing tags on Steam. Never throws when the store fails
    /// (rate limit, network: <see cref="StoreLookup"/>): what was not reached
    /// is looked up next time. Only a cancellation by <paramref name="ct"/> throws.
    /// Returns false when Steam was asked nothing (nothing missing, or the pause after
    /// a rate limit): the tags known are the same.
    /// </summary>
    /// <param name="countsInTaste">
    /// The played games looked up: those of the taste, the player's choices included
    /// (<see cref="TasteSelection.Counts"/>).
    /// </param>
    /// <param name="progress">Receives (done, total) during the lookups.</param>
    public async Task<bool> FetchMissingAsync(
        IEnumerable<PlayedGame> played,
        IEnumerable<CatalogGame> candidates,
        SteamTagClient client,
        Func<PlayedGame, bool> countsInTaste,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var playedList = played.ToList();
        var candidateList = candidates.ToList();

        await _fetchLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsPaused)
                return false;
            var now = _time.GetUtcNow();
            // Computed inside the lock: a cancelled run may have found some meanwhile.
            var (appIds, titles, titleIfNoTags, wanted) = Missing(LoadIndex(candidateList), playedList, candidateList, now, countsInTaste);
            int total = appIds.Count + titles.Count;
            if (total == 0)
                return false;

            int done = 0;
            try
            {
                // Steam games by app id: 100 per request.
                foreach (var batch in appIds.Chunk(SteamTagClient.BatchSize))
                {
                    progress?.Report((done, total));
                    var found = await client.FetchByAppIdsAsync(batch, ct).ConfigureAwait(false);
                    foreach (string appId in batch)
                    {
                        var tags = found.GetValueOrDefault(appId);
                        Set(AppKey(appId), new TagEntry(tags ?? new Dictionary<int, double>(), now));
                        // No tags (no longer sold): searched by title too, in this pass.
                        if (tags is null && titleIfNoTags.TryGetValue(appId, out var byTitle) && wanted.Add(byTitle.Key))
                        {
                            titles.Add(byTitle);
                            total++;
                        }
                    }
                    done += batch.Length;
                }

                // The others by title: one search each, TitleSearchesAtOnce at a time.
                // Counted, reported and saved along under one lock: the progress never goes back.
                var progressLock = new object();
                progress?.Report((done, total));
                await Parallel.ForEachAsync(titles,
                    new ParallelOptions { MaxDegreeOfParallelism = TitleSearchesAtOnce, CancellationToken = ct },
                    async (entry, token) =>
                    {
                        var found = await client.SearchByTitleAsync(entry.Title, token).ConfigureAwait(false);
                        // Remembered even when not found, to be retried in 30 days only.
                        Set(entry.Key, new TagEntry(found?.Tags ?? new Dictionary<int, double>(), now));
                        bool save;
                        lock (progressLock)
                        {
                            progress?.Report((++done, total));
                            save = done % SaveEvery == 0;
                        }
                        if (save)
                            await SaveAsync().ConfigureAwait(false);
                    }).ConfigureAwait(false);
                progress?.Report((total, total));
            }
            catch (Exception ex) when (StoreLookup.Failed(ex, ct))
            {
                // Rate limit or store down: the rest waits for the next time; after a rate
                // limit, not before a pause (asked again at once, it would only refuse again).
                PauseIfRateLimited(ex);
            }
            finally
            {
                // Saved even after an error or a cancellation: nothing found is lost.
                await SaveAsync().ConfigureAwait(false);
            }
            return true;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>
    /// Names of the tags in a Steam language ("french", "english"), for the
    /// explanations on the cards. Tags are compared by id: the language only
    /// changes what is displayed. Cached 30 days per language; on a network
    /// error, the old names (or none) are returned.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, string>> GetTagNamesAsync(
        SteamTagClient client, string steamLanguage = "french", CancellationToken ct = default)
    {
        string cacheKey = TagNamesCacheKey(steamLanguage);
        var cached = _cache.TryLoad(cacheKey);
        IReadOnlyDictionary<int, string>? names = null;
        if (cached is not null)
        {
            try
            {
                names = JsonSerializer.Deserialize(cached.Content, SteamJsonContext.Default.DictionaryInt32String);
            }
            catch (JsonException)
            {
            }
        }
        var now = _time.GetUtcNow();
        if (names is not null && now - cached!.SavedAt < TagNamesMaxAge)
            return names;
        // Paused after a rate limit, or a failure just now: the old names (or none), Steam is asked nothing.
        if (IsPaused || now.UtcTicks < Interlocked.Read(ref _namesRetryAfterTicks))
            return names ?? new Dictionary<int, string>();

        try
        {
            var fresh = await client.FetchTagNamesAsync(steamLanguage, ct).ConfigureAwait(false);
            await _cache.SaveAsync(cacheKey, JsonSerializer.Serialize(fresh, SteamJsonContext.Default.IReadOnlyDictionaryInt32String), ct).ConfigureAwait(false);
            return fresh;
        }
        // Steam unreachable: the old names (or none). A cancelled computation goes up.
        catch (Exception ex) when (StoreLookup.Failed(ex, ct))
        {
            // A rate limit pauses the tag lookups too, like FetchMissingAsync's.
            PauseIfRateLimited(ex);
            Interlocked.Exchange(ref _namesRetryAfterTicks, (_time.GetUtcNow() + RateLimitPause).UtcTicks);
            return names ?? new Dictionary<int, string>();
        }
    }

    /// <summary>
    /// App ids and (cache key, title) pairs to look up, favorites first, then the
    /// candidates; and the title to search for each app id that gives no tags.
    /// </summary>
    private static (List<string> AppIds, List<(string Key, string Title)> Titles, Dictionary<string, (string Key, string Title)> TitleIfNoTags, HashSet<string> Seen) Missing(
        SteamTagIndex index, IEnumerable<PlayedGame> played, IEnumerable<CatalogGame> candidates, DateTimeOffset now,
        Func<PlayedGame, bool> countsInTaste)
    {
        var appIds = new List<string>();
        var titles = new List<(string Key, string Title)>();
        var titleIfNoTags = new Dictionary<string, (string Key, string Title)>();
        var seen = new HashSet<string>();

        void Want(string key, Action add)
        {
            if (seen.Add(key) && index.NeedsLookup(key, now))
                add();
        }

        // Every game that counts in the taste (the profile is learned on
        // tags too), the favorites first: they also open the sections.
        foreach (var game in played
            .Where(countsInTaste)
            .OrderByDescending(g => TasteProfile.Engagement(g, now) >= SimilarityRecommender.FavoriteEngagement))
        {
            if (index.For(game) is not null)
                continue;
            // Steam games, and the games added by hand found on Steam: by app id,
            // and by title should the app id give nothing.
            if (game.SteamAppId is { } appId && index.NeedsLookup(AppKey(appId), now))
            {
                Want(AppKey(appId), () => appIds.Add(appId));
                if (TitleKey(game.Name) is { } titleKey && index.NeedsLookup(titleKey, now))
                    titleIfNoTags.TryAdd(appId, (titleKey, game.Name));
            }
            // Every other game (Xbox, PlayStation, added by hand) by title, and a
            // Steam version no longer sold (its app id gives no tags: "Titan Quest",
            // whose "Anniversary Edition" is sold).
            else if (TitleKey(game.Name) is { } key)
                Want(key, () => titles.Add((key, game.Name)));
        }

        // The Game Pass candidates, and the GOG, Epic and PlayStation Store ones Steam
        // does not sell (those on Steam take the tags of their Steam entry); the player's
        // games built from their history ("Mes jeux": by app id, else by title).
        foreach (var game in candidates.Where(g => g.PrimaryStore
            is CatalogSource.GamePass or CatalogSource.Gog or CatalogSource.Epic or CatalogSource.PlayStationStore or CatalogSource.MyGames))
        {
            if (index.For(game) is not null)
                continue;
            if (game.SteamAppId is { } appId)
                Want(AppKey(appId), () => appIds.Add(appId));
            else if (TitleKey(game.Title) is { } key)
                Want(key, () => titles.Add((key, game.Title)));
        }
        return (appIds, titles, titleIfNoTags, seen);
    }

    internal static string AppKey(string appId) => "app:" + appId;

    internal static string? TitleKey(string title) =>
        GameTitle.Key(title) is { Length: > 0 } name ? "title:" + name : null;

    private Dictionary<string, TagEntry> Entries()
    {
        if (_entries is not null)
            return _entries;

        var loaded = _cache.LoadDictionary(CacheKey, SteamJsonContext.Default.DictionaryStringTagEntry);
        // Two first calls racing: whichever wins, both see the same dictionary.
        Interlocked.CompareExchange(ref _entries, loaded, null);
        return _entries;
    }

    private void Set(string key, TagEntry value)
    {
        var entries = Entries();
        lock (entries)
            entries[key] = value;
    }

    private Task SaveAsync()
    {
        var entries = Entries();
        string json;
        lock (entries)
            json = JsonSerializer.Serialize(entries, SteamJsonContext.Default.DictionaryStringTagEntry);
        // Not cancellable: a half-written cache would be worse than a late one.
        return _cache.SaveAsync(CacheKey, json, CancellationToken.None);
    }
}

/// <summary>Cached tags of one game (by app id or by title). Empty = not found on Steam.</summary>
internal sealed record TagEntry(IReadOnlyDictionary<int, double> Tags, DateTimeOffset CheckedAt);

/// <summary>The tags known at one moment, for played games and candidates. Read only.</summary>
public sealed class SteamTagIndex
{
    private readonly IReadOnlyDictionary<string, TagEntry> _entries;
    // The Steam store candidates know their tags: they answer for games
    // with the same Steam app id or the same title, at no cost.
    private readonly Dictionary<string, IReadOnlyDictionary<int, double>> _fromCatalog = new();

    // The player's tags per title key: a copy, as the computation reads the
    // index off the UI thread while a click changes the settings.
    private readonly Dictionary<string, int[]> _added;

    internal SteamTagIndex(IReadOnlyDictionary<string, TagEntry> entries, IEnumerable<CatalogGame> candidates,
        IReadOnlyDictionary<string, List<int>>? addedTags = null)
    {
        _entries = entries;
        _added = Copy(addedTags);
        foreach (var game in candidates)
        {
            if (game.Tags is not { Count: > 0 } tags)
                continue;
            if (game.SteamAppId is { } appId)
                _fromCatalog.TryAdd(SteamTagSource.AppKey(appId), tags);
            if (SteamTagSource.TitleKey(game.Title) is { } titleKey)
                _fromCatalog.TryAdd(titleKey, tags);
        }
    }

    private SteamTagIndex(SteamTagIndex index, IReadOnlyDictionary<string, List<int>> addedTags)
    {
        _entries = index._entries;
        _fromCatalog = index._fromCatalog;
        _added = Copy(addedTags);
    }

    private static Dictionary<string, int[]> Copy(IReadOnlyDictionary<string, List<int>>? addedTags) =>
        addedTags?.Where(e => e.Value.Count > 0).ToDictionary(e => e.Key, e => e.Value.ToArray()) ?? [];

    /// <summary>The same index with other tags added by the player (just changed).</summary>
    public SteamTagIndex WithAdded(IReadOnlyDictionary<string, List<int>> addedTags) => new(this, addedTags);

    public IReadOnlyDictionary<int, double>? For(CatalogGame game)
    {
        if (game.Tags is { Count: > 0 } own)
            return SteamTagClient.StyleOnly(own);
        if (game.SteamAppId is { } appId && Find(SteamTagSource.AppKey(appId)) is { } byApp)
            return byApp;
        return SteamTagSource.TitleKey(game.Title) is { } key ? Find(key) : null;
    }

    /// <summary>
    /// Its tags on Steam only, what is looked up: by app id, else by title (a
    /// game of another platform, or a Steam version no longer sold).
    /// </summary>
    public IReadOnlyDictionary<int, double>? For(PlayedGame game)
    {
        if (game.SteamAppId is { } appId && Find(SteamTagSource.AppKey(appId)) is { } byApp)
            return byApp;
        return SteamTagSource.TitleKey(game.Name) is { } key ? Find(key) : null;
    }

    /// <summary>
    /// The tags of one game played on several platforms (same <see cref="GameTitle.Key"/>):
    /// those the player added first, as strong as its most voted Steam tag (1),
    /// then the Steam tags of the first platform Steam knows (the Steam line
    /// knows them by app id, the Xbox one may not). Null when it has neither.
    /// </summary>
    public IReadOnlyDictionary<int, double>? ForGame(IReadOnlyCollection<PlayedGame> platforms)
    {
        var steam = platforms.Select(For).FirstOrDefault(t => t is not null);
        if (_added.Count == 0 || platforms.Count == 0 || !_added.TryGetValue(GameTitle.Key(platforms.First().Name), out var added))
            return steam;

        // Added first, in their order: TopNames (a stable sort) shows them first.
        var merged = new Dictionary<int, double>();
        foreach (int tag in added)
            merged.TryAdd(tag, 1.0);
        foreach (var (tag, weight) in steam ?? new Dictionary<int, double>())
            merged.TryAdd(tag, weight);
        return merged;
    }

    /// <summary>
    /// The names of a game's most voted tags (<paramref name="count"/> at most),
    /// for display: an id without a name in the app's language is skipped, and
    /// equal votes keep Steam's order.
    /// </summary>
    public static IReadOnlyList<string> TopNames(IReadOnlyDictionary<int, double>? tags, IReadOnlyDictionary<int, string>? names, int count = 3) =>
        TopTags(tags, names, count).Select(t => t.Name).ToList();

    /// <summary>The same tags as <see cref="TopNames"/>, with their ids (to tell those the player added).</summary>
    public static IReadOnlyList<(int Id, string Name)> TopTags(IReadOnlyDictionary<int, double>? tags, IReadOnlyDictionary<int, string>? names, int count = 3) =>
        tags is null || names is null ? [] : tags
            .OrderByDescending(t => t.Value)
            .Where(t => names.ContainsKey(t.Key))
            .Select(t => (t.Key, names[t.Key]))
            .Take(count)
            .ToList();

    /// <summary>Unknown, or not found and not retried for 30 days.</summary>
    internal bool NeedsLookup(string key, DateTimeOffset now) =>
        !_fromCatalog.ContainsKey(key)
        && (!_entries.TryGetValue(key, out var entry)
            || (entry.Tags.Count == 0 && now - entry.CheckedAt > SteamTagSource.RetryNotFoundAfter));

    private IReadOnlyDictionary<int, double>? Find(string key)
    {
        if (_fromCatalog.TryGetValue(key, out var tags))
            return SteamTagClient.StyleOnly(tags);
        return _entries.TryGetValue(key, out var entry) && entry.Tags.Count > 0 ? SteamTagClient.StyleOnly(entry.Tags) : null;
    }
}
