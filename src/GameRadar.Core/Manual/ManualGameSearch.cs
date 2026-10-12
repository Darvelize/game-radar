using GameRadar.Core.GamePass;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;

namespace GameRadar.Core.Manual;

/// <summary>A game proposed when the player types a title in "Ajouter un jeu".</summary>
/// <param name="SteamAppId">Its Steam app id: its genres and tags will be found there.</param>
/// <param name="OnXbox">Sold on the Microsoft Store (Xbox): found there, whatever its Game Pass status.</param>
/// <param name="OnPlayStation">Found on the PlayStation Store or in the PS Plus catalog.</param>
/// <param name="PlayStationProductId">Its PlayStation Store product id: its genres are asked there when it is added.</param>
/// <param name="Genres">Its genres when already known (the PS Plus catalog lists them), in the common vocabulary.</param>
public sealed record ManualGameMatch(
    string Title,
    string? SteamAppId,
    bool InGamePass,
    bool OnSteam,
    bool OnXbox = false,
    bool OnPlayStation = false,
    string? PlayStationProductId = null,
    IReadOnlyList<string>? Genres = null);

/// <summary>
/// Finds the game the player means: the Game Pass catalog already loaded
/// (instant, offline), then the Steam store search, one result per game
/// (<see cref="GameTitle.Key"/>: "Hades" on both is one line).
/// </summary>
public static class ManualGameSearch
{
    /// <summary>
    /// Catalog games whose title contains what was typed (case, accents and
    /// symbols ignored): those starting with it first, then the shortest.
    /// Give the catalog seen by the player's plan (<see cref="CatalogGame.ForPlan"/>):
    /// a game outside it is still found, but not offered as Game Pass.
    /// </summary>
    public static IReadOnlyList<ManualGameMatch> InCatalog(IEnumerable<CatalogGame> catalog, string typed, int max = 5) =>
        Find(catalog, typed, max)
            .Select(g => new ManualGameMatch(GameTitle.Clean(g.Title), g.SteamAppId, g.InGamePass, OnSteam: g.SteamAppId is not null))
            .ToList();

    /// <summary>
    /// The same search in the PS Plus catalog (give the games of the player's
    /// plan): a PlayStation game, with the genres the catalog lists. The
    /// fallback of the PlayStation Store search, and a complement of it.
    /// </summary>
    public static IReadOnlyList<ManualGameMatch> InPsPlusCatalog(IEnumerable<CatalogGame> catalog, string typed, int max = 5) =>
        Find(catalog, typed, max)
            .Select(g => new ManualGameMatch(GameTitle.Clean(g.Title), SteamAppId: null, InGamePass: false, OnSteam: false,
                OnPlayStation: true, Genres: g.Genres.Count > 0 ? g.Genres : null))
            .ToList();

    private static IEnumerable<CatalogGame> Find(IEnumerable<CatalogGame> catalog, string typed, int max)
    {
        string wanted = CatalogMatcher.NormalizeName(typed);
        if (wanted.Length == 0)
            return [];

        return catalog
            .Select(g => (Game: g, Name: CatalogMatcher.NormalizeName(g.Title)))
            .Where(g => g.Name.Contains(wanted, StringComparison.Ordinal))
            // Editions of one game: a single line.
            .GroupBy(g => GameTitle.Key(g.Game.Title))
            .Select(group => group.First())
            .OrderByDescending(g => g.Name.StartsWith(wanted, StringComparison.Ordinal))
            .ThenBy(g => g.Name.Length)
            .Take(max)
            .Select(g => g.Game)
            .ToList();
    }

    /// <summary>
    /// The catalog matches first, then Steam's (in Steam's order: relevance);
    /// a game in both is one line, which takes the Steam app id. Two Steam
    /// games with one title (DOOM 2016, DOOM 1993): the first, most relevant, keeps it.
    /// </summary>
    public static IReadOnlyList<ManualGameMatch> Merge(
        IReadOnlyList<ManualGameMatch> fromCatalog,
        IReadOnlyList<SteamSuggestion> fromSteam,
        IReadOnlyList<StoreGameSuggestion>? fromXboxStore = null,
        IReadOnlyList<ManualGameMatch>? fromPsPlus = null,
        IReadOnlyList<PlayStationStoreGame>? fromPlayStationStore = null)
    {
        var result = fromCatalog.ToList();

        // A game already there (same title key) is updated, else added at the end.
        void Upsert(string title, Func<ManualGameMatch> create, Func<ManualGameMatch, ManualGameMatch> update)
        {
            string key = GameTitle.Key(title);
            int same = result.FindIndex(m => GameTitle.Key(m.Title) == key);
            if (same < 0)
                result.Add(create());
            else
                result[same] = update(result[same]);
        }

        foreach (var suggestion in fromSteam)
            Upsert(suggestion.Name,
                () => new ManualGameMatch(suggestion.Name, suggestion.AppId, InGamePass: false, OnSteam: true),
                m => m.OnSteam ? m : m with { SteamAppId = suggestion.AppId, OnSteam = true });

        // The Microsoft Store last: it names the Xbox games that are neither
        // in the Game Pass nor on Steam (an exclusive). An edition of a game
        // already there ("Standard Edition") is that game.
        foreach (var suggestion in fromXboxStore ?? [])
            Upsert(suggestion.Title,
                () => new ManualGameMatch(GameTitle.Clean(suggestion.Title), SteamAppId: null, InGamePass: false, OnSteam: false, OnXbox: true),
                m => m with { OnXbox = true });

        // The PS Plus catalog (local, with its genres), then the PlayStation Store
        // (the product id that gives the genres of a game the catalog lacks).
        foreach (var match in fromPsPlus ?? [])
            Upsert(match.Title, () => match, m => m with { OnPlayStation = true, Genres = m.Genres ?? match.Genres });

        // The first line of a game, the most relevant, gives the product id; editions and
        // console versions of it ("Digital Deluxe", PS4 and PS5) are the same game.
        foreach (var game in fromPlayStationStore ?? [])
            Upsert(game.Title,
                () => new ManualGameMatch(GameTitle.Clean(game.Title), SteamAppId: null, InGamePass: false, OnSteam: false,
                    OnPlayStation: true, PlayStationProductId: game.ProductId),
                m => m with { OnPlayStation = true, PlayStationProductId = m.PlayStationProductId ?? game.ProductId });
        return result;
    }
}
