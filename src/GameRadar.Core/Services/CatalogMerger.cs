using GameRadar.Core.Models;

namespace GameRadar.Core.Services;

/// <summary>
/// Puts the Game Pass catalog, the PS Plus catalog, the Steam store, the
/// GOG, Epic and PlayStation Store candidates together, one entry per game. Platforms share no id: games are
/// compared by base title (edition and platform suffixes dropped, as for the
/// recommendations).
/// </summary>
public static class CatalogMerger
{
    /// <summary>
    /// Every Game Pass game, then the PS Plus games it does not include, then
    /// the Steam games missing from both, each marked with its Steam app id
    /// when Steam sells it too. A game in several keeps its Game Pass entry,
    /// else its PS Plus one: the genres of the Microsoft Store (or of the
    /// PlayStation catalog, in the same vocabulary) are the profile's own,
    /// and the subscription is the point of the app.
    /// </summary>
    /// <param name="gamePass">
    /// The Game Pass catalog as the player's plan sees it (<see cref="CatalogGame.ForPlan"/>):
    /// games out of the plan are left out, and those sold on Steam come back
    /// as Steam games (with their Steam rating and picture).
    /// </param>
    /// <param name="psPlus">
    /// The PS Plus catalog as the player's plan sees it (<see cref="CatalogGame.ForPsPlusPlan"/>),
    /// the same way. Without it, the result is what it was before PS Plus.
    /// </param>
    /// <param name="gog">
    /// The GOG store candidates: a game already there (any source) gets its GOG
    /// page, the others come last. Without it, the result is what it was before GOG.
    /// </param>
    /// <param name="epic">The Epic store candidates, after GOG and the same way (a game also on GOG keeps its GOG entry).</param>
    /// <param name="psStore">
    /// The PlayStation Store best sellers, last and the same way: a game of the
    /// player's PS Plus plan keeps its PS Plus entry; out of the plan, it comes back as a store game.
    /// </param>
    public static IReadOnlyList<CatalogGame> Combine(
        IReadOnlyList<CatalogGame> gamePass, IReadOnlyList<CatalogGame> steam, IReadOnlyList<CatalogGame>? psPlus = null,
        IReadOnlyList<CatalogGame>? gog = null, IReadOnlyList<CatalogGame>? epic = null, IReadOnlyList<CatalogGame>? psStore = null)
    {
        var combined = CombineSubscriptionsAndSteam(gamePass, steam, psPlus);
        combined = AddStore(combined, gog, (game, onGog) => game with { GogStoreLink = onGog.GogStoreLink });
        combined = AddStore(combined, epic, (game, onEpic) => game with { EpicStoreLink = onEpic.EpicStoreLink });
        return AddStore(combined, psStore, (game, onPs) => game with { PlayStationProductId = onPs.PlayStationProductId });
    }

    /// <summary>
    /// A PC store's candidates: a game sold elsewhere keeps its entry
    /// (subscription, Steam tags and rating) and gains the store's page
    /// (<paramref name="withLink"/>); the others come last. The first entry of
    /// a title wins (best seller first).
    /// </summary>
    private static IReadOnlyList<CatalogGame> AddStore(
        IReadOnlyList<CatalogGame> combined, IReadOnlyList<CatalogGame>? store, Func<CatalogGame, CatalogGame, CatalogGame> withLink)
    {
        if (store is null || store.Count == 0)
            return combined;

        var byTitle = new Dictionary<string, CatalogGame>();
        foreach (var game in store)
            byTitle.TryAdd(GameTitle.Key(game.Title), game);

        var result = new List<CatalogGame>(combined.Count + byTitle.Count);
        var matched = new HashSet<string>();
        foreach (var game in combined)
        {
            string key = GameTitle.Key(game.Title);
            if (byTitle.TryGetValue(key, out var onStore))
            {
                result.Add(withLink(game, onStore));
                matched.Add(key);
            }
            else
            {
                result.Add(game);
            }
        }
        result.AddRange(byTitle.Where(pair => !matched.Contains(pair.Key)).Select(pair => pair.Value));
        return result;
    }

    private static IReadOnlyList<CatalogGame> CombineSubscriptionsAndSteam(
        IReadOnlyList<CatalogGame> gamePass, IReadOnlyList<CatalogGame> steam, IReadOnlyList<CatalogGame>? psPlus)
    {
        gamePass = gamePass.Where(g => g.InGamePass).ToList();
        var playStation = (psPlus ?? []).Where(g => g.InPsPlus).ToList();
        if (steam.Count == 0 && playStation.Count == 0)
            return gamePass;

        // TryAdd: the first Steam entry of a title wins (best seller first).
        var steamByTitle = new Dictionary<string, CatalogGame>();
        foreach (var game in steam)
            steamByTitle.TryAdd(GameTitle.Key(game.Title), game);
        var psPlusByTitle = new Dictionary<string, CatalogGame>();
        foreach (var game in playStation)
            psPlusByTitle.TryAdd(GameTitle.Key(game.Title), game);

        var matchedOnSteam = new HashSet<string>();
        var gamePassKeys = new HashSet<string>();
        var combined = new List<CatalogGame>(gamePass.Count + playStation.Count + steam.Count);
        foreach (var game in gamePass)
        {
            string key = GameTitle.Key(game.Title);
            gamePassKeys.Add(key);
            var entry = game;
            // "with": copy of the immutable record with the other store's fields.
            if (psPlusByTitle.TryGetValue(key, out var onPsPlus))
                entry = entry with { PsPlusTiers = onPsPlus.PsPlusTiers, PlayStationConceptId = onPsPlus.PlayStationConceptId };
            if (steamByTitle.TryGetValue(key, out var onSteam))
            {
                // The app id for the link, the tags for the similarity search.
                entry = entry with { SteamAppId = onSteam.SteamAppId, Tags = onSteam.Tags };
                matchedOnSteam.Add(key);
            }
            combined.Add(entry);
        }

        foreach (var (key, game) in psPlusByTitle)
        {
            // Already there, with its PS Plus plans.
            if (gamePassKeys.Contains(key))
                continue;

            var entry = game;
            if (steamByTitle.TryGetValue(key, out var onSteam))
            {
                // A PS Plus game has no rating or play modes of its own: Steam's will do.
                entry = entry with
                {
                    SteamAppId = onSteam.SteamAppId,
                    Tags = onSteam.Tags,
                    AverageRating = onSteam.AverageRating,
                    RatingCount = onSteam.RatingCount,
                    Modes = onSteam.Modes,
                    Genres = entry.Genres.Count > 0 ? entry.Genres : onSteam.Genres,
                    ImageUrl = entry.ImageUrl ?? onSteam.ImageUrl
                };
                matchedOnSteam.Add(key);
            }
            combined.Add(entry);
        }

        combined.AddRange(steam.Where(g => !matchedOnSteam.Contains(GameTitle.Key(g.Title))));
        return combined;
    }
}
