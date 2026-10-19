using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.GOG;

/// <summary>
/// Gives the GOG history: the pages of the public profile, cached raw (one
/// JSON array of the pages as GOG sent them) and read again at each load.
/// GOG is only called on explicit refresh by the user.
/// </summary>
public sealed class GogHistorySource
{
    private const string HistoryKey = "gog-history";

    // ponytail: 100 pages of 50 = 5,000 games, a bound against an absurd page count; raise it if a library ever reaches it.
    private const int MaxPages = 100;

    private readonly CacheStore _cache;

    public GogHistorySource(CacheStore cache)
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
            return new GameHistory(ToPlayedGames(ParsePages(entry.Content)), entry.SavedAt);
        }
        catch (Exception ex) when (ex is JsonException or GogException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches every page of the profile (1 request per 50 games) and replaces
    /// the cache. Throws <see cref="GogException"/> with an explanation
    /// (profile not public, no such profile...): the old cache is kept.
    /// </summary>
    public async Task<GameHistory> RefreshAsync(GogClient client, string user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var pages = new List<string>();
        var games = new List<GogLibraryGame>();
        int pageCount = 1;
        for (int page = 1; page <= Math.Min(pageCount, MaxPages); page++)
        {
            string json = await client.FetchPageJsonAsync(user, page, ct).ConfigureAwait(false);
            // Parsed BEFORE saving: an unexpected answer throws here and the
            // previous, valid cache is kept.
            var parsed = GogClient.ParseJson(json);
            pageCount = parsed.Pages;
            pages.Add(json);
            games.AddRange(parsed.Games);
        }

        await _cache.SaveAsync(HistoryKey, CacheStore.JoinRaw(pages), ct).ConfigureAwait(false);
        return new GameHistory(ToPlayedGames(games), DateTimeOffset.UtcNow);
    }

    /// <summary>The cached array of raw pages → the games of every page.</summary>
    private static List<GogLibraryGame> ParsePages(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new GogException(CoreStrings.Gog_ProfileFormat);
        return document.RootElement.EnumerateArray()
            .SelectMany(page => GogClient.ParseJson(page.GetRawText()).Games)
            .ToList();
    }

    /// <summary>
    /// Library → games, most recently played first. Only an achievement
    /// percentage is known (no counts). A game GOG Galaxy never followed is
    /// "never launched" (0 min), as a Steam game: no taste signal, and still
    /// recommendable (the user's choice: a forgotten game; a wrong guess is
    /// sent away with "Pas intéressé").
    /// </summary>
    internal static IReadOnlyList<PlayedGame> ToPlayedGames(IEnumerable<GogLibraryGame> library) =>
        library
            // A game may move from one page to the next between two requests.
            .DistinctBy(g => g.Id)
            .Select(game => new PlayedGame(
                Platform: GogClient.PlatformName,
                PlatformId: game.Id,
                Name: game.Title,
                HasAchievements: game.AchievementsPercentage is not null,
                UnlockedAchievements: 0,
                TotalAchievements: null,
                ProgressPercentage: game.AchievementsPercentage ?? 0,
                LastPlayed: game.LastSession,
                Status: game.AchievementsPercentage is { } percentage
                    ? StatusClassifier.FromAchievements(0, null, percentage)
                    : PlayStatus.Played,
                PlaytimeMinutes: game.PlaytimeMinutes ?? 0)
            {
                ImageUrl = game.ImageUrl
            })
            .OrderByDescending(g => g.LastPlayed)
            .ToList();
}
