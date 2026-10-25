using GameRadar.Core.Models;
using GameRadar.Core.Steam;
using GameRadar.Core.Xbox;

namespace GameRadar.Core.Services;

/// <summary>
/// "Mes jeux" (the user's choice, 19/10/2026): the player's games not finished yet (neither
/// "Histoire finie" nor 100 %) nor counted in the taste (the heart of the history: a game is
/// never recommended because of itself), one per title, as candidates scored like the others.
/// Every platform counts, even one where a subscription may have launched the game.
/// </summary>
public static class OwnedGames
{
    /// <summary>
    /// The player's games not finished yet nor counted in the taste: the lines of each, by title
    /// (<see cref="HistoryMerger.GroupByGame"/>: the most recently played line first).
    /// </summary>
    /// <param name="history">The history with the manual "finished" marks applied (PlayStatusOverrides).</param>
    /// <param name="countsInTaste">Whether a line counts in the taste (TasteSelection.Counts, the player's choices included).</param>
    public static IReadOnlyDictionary<string, IReadOnlyList<PlayedGame>> Collect(IEnumerable<PlayedGame> history, Func<PlayedGame, bool> countsInTaste) =>
        HistoryMerger.GroupByGame(history)
            .Where(lines => lines.All(g => g.Status == PlayStatus.Played && !countsInTaste(g)))
            .ToDictionary(lines => GameTitle.Key(lines[0].Name));

    /// <summary>
    /// For a Game Pass subscriber (the user's choice, 19/10/2026): an Xbox game out of their
    /// Game Pass is likely one they played through the subscription and left it since, no
    /// longer theirs: left out when only the Xbox history has it. A game in their Game Pass
    /// (by title, or Xbox title id) stays; so does one on another platform too. Limit: an Xbox
    /// game they bought, out of the Game Pass, goes too (the history does not tell).
    /// </summary>
    /// <param name="pool">The candidates, the player's plan applied (<see cref="CatalogGame.ForPlan"/>).</param>
    public static IReadOnlyDictionary<string, IReadOnlyList<PlayedGame>> WithoutGamePassLeftovers(
        IReadOnlyDictionary<string, IReadOnlyList<PlayedGame>> owned, IReadOnlyList<CatalogGame> pool)
    {
        var inGamePass = pool.Where(g => g.InGamePass).ToList();
        var titles = inGamePass.Select(g => GameTitle.Key(g.Title)).ToHashSet();
        var titleIds = inGamePass.Select(g => g.XboxTitleId).OfType<string>().ToHashSet();
        return owned
            .Where(e => !e.Value.All(g => g.Platform == XboxLiveClient.PlatformName)
                || titles.Contains(e.Key)
                || e.Value.Any(g => titleIds.Contains(g.PlatformId)))
            .ToDictionary(e => e.Key, e => e.Value);
    }

    /// <summary>
    /// The history without the player's games not finished yet: what still leaves candidates out
    /// (RecommendationEngine's history while "Mes jeux" is ticked). A game finished under
    /// another title, matched by its ids, stays out.
    /// </summary>
    public static IReadOnlyList<PlayedGame> Others(IEnumerable<PlayedGame> history, IReadOnlyDictionary<string, IReadOnlyList<PlayedGame>> owned) =>
        history.Where(g => !owned.ContainsKey(GameTitle.Key(g.Name))).ToList();

    /// <summary>
    /// The pool with the player's games in it: the catalog's entry of a game (same title, as
    /// CatalogMerger matches stores, or same Steam app id) marked owned, keeping its badges,
    /// rating and picture; a game in no catalog added at the end, with the genres
    /// <paramref name="known"/> has for it.
    /// </summary>
    /// <param name="owned">The player's games (<see cref="Collect"/>).</param>
    /// <param name="known">Genres found for history lines (by <see cref="PlayedGame.Key"/>).</param>
    public static IReadOnlyList<CatalogGame> AddTo(
        IReadOnlyList<CatalogGame> pool,
        IReadOnlyDictionary<string, IReadOnlyList<PlayedGame>> owned,
        IEnumerable<PlayedGameWithGenres> known)
    {
        if (owned.Count == 0)
            return pool;

        // A catalog entry may have another title than the history: its Steam app id tells
        // too, and its Xbox title id (as WithoutGamePassLeftovers keeps the game by it).
        var byAppId = new Dictionary<string, string>();
        var byTitleId = new Dictionary<string, string>();
        foreach (var (key, lines) in owned)
        {
            foreach (string appId in lines.Select(g => g.SteamAppId).OfType<string>())
                byAppId.TryAdd(appId, key);
            foreach (var xbox in lines.Where(g => g.Platform == XboxLiveClient.PlatformName))
                byTitleId.TryAdd(xbox.PlatformId, key);
        }

        var result = new List<CatalogGame>(pool.Count + owned.Count);
        var marked = new HashSet<string>();
        foreach (var game in pool)
        {
            string key = GameTitle.Key(game.Title);
            if (!owned.ContainsKey(key))
                key = (game.SteamAppId is { } appId ? byAppId.GetValueOrDefault(appId) : null)
                    ?? (game.XboxTitleId is { } titleId ? byTitleId.GetValueOrDefault(titleId) : null)
                    ?? key;
            if (owned.TryGetValue(key, out var lines))
            {
                result.Add(game with { Owned = Summary(lines) });
                marked.Add(key);
            }
            else
            {
                result.Add(game);
            }
        }

        var genres = known.GroupBy(g => g.Game.Key).ToDictionary(g => g.Key, g => g.First());
        foreach (var (key, lines) in owned)
        {
            if (marked.Contains(key))
                continue;
            var withGenres = lines.Select(g => genres.GetValueOrDefault(g.Key)).FirstOrDefault(g => g is not null);
            string? appId = lines.Select(g => g.SteamAppId).FirstOrDefault(id => id is not null);
            result.Add(new CatalogGame(
                ProductId: CatalogGame.FromHistoryPrefix + key,
                Title: lines[0].Name,
                XboxTitleId: null,
                PackageFamilyName: null,
                Genres: withGenres?.Genres ?? [],
                OnPc: false,
                OnConsole: false,
                LeavingSoon: false,
                Modes: withGenres?.Modes ?? PlayModes.None,
                ImageUrl: appId is not null ? SteamCatalogClient.HeaderImageUrl(appId) : lines.Select(g => g.ImageUrl).FirstOrDefault(url => url is not null),
                SteamAppId: appId)
            {
                Owned = Summary(lines)
            });
        }
        return result;
    }

    private static OwnedGame Summary(IReadOnlyList<PlayedGame> lines)
    {
        var playtimes = lines.Where(g => g.PlaytimeMinutes is not null).Select(g => g.PlaytimeMinutes!.Value).ToList();
        var progress = lines.Where(g => g.HasAchievements).Select(g => g.ProgressPercentage).ToList();
        return new OwnedGame(
            lines.Select(g => g.Platform).Distinct().ToList(),
            playtimes.Count == 0 ? null : playtimes.Sum(),
            progress.Count == 0 ? null : progress.Max(),
            NeverPlayed: lines.All(g => g.NeverPlayed));
    }
}
