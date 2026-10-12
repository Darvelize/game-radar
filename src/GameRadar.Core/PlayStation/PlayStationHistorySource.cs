using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.PlayStation;

/// <summary>
/// Gives the PlayStation history: the games with trophies and the PS4/PS5
/// games played (raw pages, cached as-is), linked by the trophy set of each
/// played game (compact cache: a game never changes trophy set).
/// PSN is only called on explicit refresh by the user.
/// </summary>
public sealed class PlayStationHistorySource
{
    private const string HistoryKey = "playstation-history";
    private const string TrophySetsKey = "playstation-trophy-sets";


    private readonly CacheStore _cache;

    public PlayStationHistorySource(CacheStore cache)
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
            var (trophyTitles, games) = ParseHistory(entry.Content);
            return new GameHistory(ToPlayedGames(trophyTitles, games, LoadTrophySets()), entry.SavedAt, GenresOf(games));
        }
        catch (Exception ex) when (ex is JsonException or PlayStationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches both lists (2 requests for most players), then the trophy set
    /// of the games played since the last refresh only (1 request per 5
    /// games), and replaces the cache.
    /// </summary>
    public async Task<GameHistory> RefreshAsync(PlayStationClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        // Each page is parsed while fetched: an unexpected answer throws
        // before anything is saved, and the previous, valid cache is kept.
        // Both lists are independent: fetched together (the account
        // serializes the token renewal).
        var trophyPagesTask = client.FetchTrophyTitlePagesAsync(ct);
        var gamePagesTask = client.FetchGamePagesAsync(ct);
        await Task.WhenAll(trophyPagesTask, gamePagesTask).ConfigureAwait(false);
        // The raw pages, already checked, put side by side as they are: no
        // need to parse them into a document and write it back.
        string json = $$"""{"trophyTitles":[{{string.Join(",", trophyPagesTask.Result)}}],"games":[{{string.Join(",", gamePagesTask.Result)}}]}""";
        var (trophyTitles, games) = ParseHistory(json);

        var known = LoadTrophySets();
        // A game gets a trophy set when it is played: one saved with the same
        // "last played" date is still right (and so is "no trophies").
        var toFetch = games
            .Where(g => !(known.TryGetValue(g.TitleId, out var entry) && entry.LastPlayed == g.LastPlayed))
            .ToList();

        // Batch by batch, saved even when one fails (a rate limit halfway):
        // the next refresh goes on from there instead of starting over.
        var fetched = new Dictionary<string, TrophySetEntry>();
        try
        {
            foreach (var batch in toFetch.Chunk(PlayStationClient.TitleIdsPerRequest))
            {
                var sets = await client.FetchTrophySetsAsync(batch.Select(g => g.TitleId).ToList(), ct).ConfigureAwait(false);
                // Only the games of the answer: one it left out is asked
                // again next time, not kept as "no trophies" for good.
                foreach (var game in batch.Where(g => sets.ContainsKey(g.TitleId)))
                    fetched[game.TitleId] = new TrophySetEntry(sets[game.TitleId], game.LastPlayed);
            }
        }
        finally
        {
            foreach (var (titleId, entry) in fetched)
                known[titleId] = entry;
            if (fetched.Count > 0)
                await _cache.MergeDictionaryAsync(TrophySetsKey, fetched, PlayStationJsonContext.Default.DictionaryStringTrophySetEntry).ConfigureAwait(false);
        }

        await _cache.SaveAsync(HistoryKey, json, ct).ConfigureAwait(false);
        return new GameHistory(ToPlayedGames(trophyTitles, games, known), DateTimeOffset.UtcNow, GenresOf(games));
    }

    /// <summary>
    /// The genres PlayStation gives to its played games, by <see cref="PlayedGame.Key"/>
    /// (the games known by their trophies only have none).
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> GenresOf(IReadOnlyList<PlayStationGame> games) =>
        games
            .Where(g => g.Genres.Count > 0)
            .GroupBy(g => PlayedGame.KeyOf(PlayStationClient.PlatformName, g.TitleId))
            .ToDictionary(g => g.Key, g => g.First().Genres);

    /// <summary>
    /// Both lists → games, most recently played first: each played game with
    /// its trophies, then the games known by their trophies only (PS3, Vita,
    /// and PS4 games missing from the list of played games).
    /// </summary>
    internal static IReadOnlyList<PlayedGame> ToPlayedGames(
        IReadOnlyList<PlayStationTrophyTitle> trophyTitles,
        IReadOnlyList<PlayStationGame> games,
        IReadOnlyDictionary<string, TrophySetEntry> trophySets)
    {
        var trophiesById = trophyTitles
            .GroupBy(t => t.CommunicationId)
            .ToDictionary(g => g.Key, g => g.First());
        var linked = new HashSet<string>();
        var played = new List<PlayedGame>();

        foreach (var game in games.GroupBy(g => g.TitleId).Select(g => g.First()))
        {
            // A compilation may have several sets: the furthest one speaks for it.
            var trophies = (trophySets.GetValueOrDefault(game.TitleId)?.SetIds ?? [])
                .Select(id => trophiesById.GetValueOrDefault(id))
                .OfType<PlayStationTrophyTitle>()
                .MaxBy(t => t.Progress);
            if (trophies is not null)
                linked.Add(trophies.CommunicationId);

            played.Add(ToPlayedGame(game.TitleId, game.Name, trophies, game.LastPlayed ?? trophies?.LastUpdated, game.PlaytimeMinutes));
        }

        foreach (var trophies in trophiesById.Values.Where(t => !linked.Contains(t.CommunicationId)))
            played.Add(ToPlayedGame(trophies.CommunicationId, trophies.Name, trophies, trophies.LastUpdated, playtimeMinutes: null));

        return played.OrderByDescending(g => g.LastPlayed).ToList();
    }

    private static PlayedGame ToPlayedGame(
        string id, string name, PlayStationTrophyTitle? trophies, DateTimeOffset? lastPlayed, int? playtimeMinutes)
    {
        // A trophy set with no trophy counts as none.
        var set = trophies is { Defined: > 0 } ? trophies : null;
        int earned = set?.Earned ?? 0;
        double progress = set?.Progress ?? 0;

        return new PlayedGame(
            Platform: PlayStationClient.PlatformName,
            PlatformId: id,
            Name: name,
            HasAchievements: set is not null,
            UnlockedAchievements: earned,
            TotalAchievements: set?.Defined,
            ProgressPercentage: progress,
            LastPlayed: lastPlayed,
            Status: StatusOf(earned, set?.Defined, progress, trophies?.HasPlatinum == true),
            PlaytimeMinutes: playtimeMinutes);
    }

    /// <summary>
    /// "Mastered" only at 100 % of the trophies, DLC included, as on Xbox and
    /// Steam (whose achievements count their DLC too, and cannot be told
    /// apart). The platinum only covers the base game (62 % on a real one,
    /// the DLC trophies missing): the game was finished, "Completed", which
    /// weighs as much in the taste (the user's choice).
    /// </summary>
    internal static PlayStatus StatusOf(int earned, int? defined, double progress, bool hasPlatinum) =>
        StatusClassifier.FromAchievements(earned, defined, progress) is PlayStatus.Mastered ? PlayStatus.Mastered
        : hasPlatinum ? PlayStatus.Completed
        : PlayStatus.Played;

    private static (IReadOnlyList<PlayStationTrophyTitle> TrophyTitles, IReadOnlyList<PlayStationGame> Games) ParseHistory(string json)
    {
        var history = JsonSerializer.Deserialize(json, PlayStationJsonContext.Default.PlayStationHistoryDto);
        if (history?.TrophyTitles is null || history.Games is null)
            throw new PlayStationException(CoreStrings.PlayStation_Format);

        return (
            history.TrophyTitles.SelectMany(PlayStationClient.ParseTrophyTitles).ToList(),
            history.Games.SelectMany(PlayStationClient.ParseGames).ToList());
    }

    private Dictionary<string, TrophySetEntry> LoadTrophySets() => _cache.LoadDictionary(TrophySetsKey, PlayStationJsonContext.Default.DictionaryStringTrophySetEntry);

    /// <summary>Trophy sets of one played game at a given "last played" date. Empty = no trophies.</summary>
    internal sealed record TrophySetEntry(IReadOnlyList<string> SetIds, DateTimeOffset? LastPlayed);
}
