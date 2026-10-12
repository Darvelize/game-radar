using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.Settings;

/// <summary>
/// One-time move of the settings saved with French game titles (1.2.0 and
/// before) to the English titles every source now gives. Two settings hold a
/// title: the refusals ("Pas intéressé", keyed by normalized title) and the
/// games added by hand (their title, and their id when they have no Steam
/// app id). Both are translated through the former French catalogs, still
/// on disk under their old cache keys, matched with the new English ones by
/// product id.
/// </summary>
/// <remarks>
/// Two steps, so a crash or a failed save loses nothing: <see cref="Apply"/>
/// translates the settings in memory; once they are saved,
/// <see cref="TrimFormerCatalogsAsync"/> deletes a former catalog only when no
/// setting still waits for one of its games (a game out of today's catalog,
/// e.g. off the Steam best sellers, is translated the day it comes back).
/// </remarks>
public static class EnglishTitlesMigration
{
    // The cache keys of the catalogs with French titles (Game Pass v5, Steam v2).
    internal const string FrenchGamePassKey = "gamepass-catalog-fr-v5";
    internal const string FrenchSteamKey = "steam-catalog-fr-v2";

    /// <summary>A former French catalog and today's English one: read once, used by both steps.</summary>
    public sealed record FormerCatalog(string CacheKey, IReadOnlyList<CatalogGame> French, IReadOnlyList<CatalogGame> English);

    /// <summary>
    /// The former French catalogs still on disk whose English one is given
    /// (null = not loaded yet: that one waits). Empty once the move is done:
    /// two missing files, nothing else read.
    /// </summary>
    public static IReadOnlyList<FormerCatalog> ReadFormerCatalogs(CacheStore cache, GameCatalog? englishGamePass, GameCatalog? englishSteam)
    {
        var former = new List<FormerCatalog>();
        foreach (var (frenchKey, english) in new[] { (FrenchGamePassKey, englishGamePass), (FrenchSteamKey, englishSteam) })
        {
            // A corrupted former catalog says nothing: nothing to move.
            if (english is not null && cache.LoadJson(frenchKey, CoreJsonContext.Default.GameCatalog) is { } french)
                former.Add(new FormerCatalog(frenchKey, french.Games, english.Games));
        }
        return former;
    }

    /// <summary>
    /// Translates the settings saved with French titles, through the former
    /// catalogs. Deletes nothing. Returns true when the settings changed and
    /// are to be saved (then <see cref="TrimFormerCatalogsAsync"/>).
    /// </summary>
    public static bool Apply(AppSettings settings, IReadOnlyList<FormerCatalog> former)
    {
        // One table for both catalogs, applied once: a setting is never
        // translated twice (A → B by the Game Pass, then B → C by Steam).
        var toEnglish = new Dictionary<string, string>();
        foreach (var catalog in former)
            foreach (var (key, title) in Translations(catalog.French, catalog.English))
                toEnglish.TryAdd(key, title);

        return Apply(settings, toEnglish);
    }

    /// <summary>
    /// Deletes each former French catalog that no setting still needs; of the
    /// others, keeps only the games a setting waits for (out of today's
    /// English catalog), so they are not read whole at every start for
    /// months. Call it once the translated settings are saved.
    /// </summary>
    public static async Task TrimFormerCatalogsAsync(AppSettings settings, CacheStore cache, IReadOnlyList<FormerCatalog> former)
    {
        var titleKeys = settings.NotInterested.Keys
            .Concat(settings.ManualGames.Select(g => GameTitle.Key(g.Title)))
            .ToHashSet();
        foreach (var catalog in former)
        {
            var englishIds = catalog.English.Select(g => g.ProductId).ToHashSet();
            var waiting = catalog.French
                .Where(g => !englishIds.Contains(g.ProductId) && titleKeys.Contains(GameTitle.Key(g.Title)))
                .ToList();
            if (waiting.Count == 0)
                cache.Delete(catalog.CacheKey);
            else if (waiting.Count < catalog.French.Count)
                await cache.SaveAsync(catalog.CacheKey, JsonSerializer.Serialize(new GameCatalog(waiting, DateTimeOffset.UtcNow), CoreJsonContext.Default.GameCatalog))
                    .ConfigureAwait(false);
        }
    }

    // French title key → English title, through the product id; only the
    // titles that change (the first one for a key seen twice).
    private static IEnumerable<(string Key, string Title)> Translations(
        IReadOnlyList<CatalogGame> frenchGames, IReadOnlyList<CatalogGame> englishGames)
    {
        var englishTitles = englishGames.GroupBy(g => g.ProductId).ToDictionary(g => g.Key, g => g.First().Title);
        return frenchGames
            .Where(g => englishTitles.ContainsKey(g.ProductId))
            .Select(g => (Key: GameTitle.Key(g.Title), Title: englishTitles[g.ProductId]))
            .Where(t => t.Key.Length > 0 && t.Key != GameTitle.Key(t.Title))
            .DistinctBy(t => t.Key);
    }

    private static bool Apply(AppSettings settings, IReadOnlyDictionary<string, string> toEnglish)
    {
        bool changed = false;
        foreach (var (key, _) in settings.NotInterested.ToList())
        {
            if (toEnglish.TryGetValue(key, out var english))
            {
                settings.NotInterested.Remove(key);
                settings.NotInterested[GameTitle.Key(english)] = english;
                changed = true;
            }
        }

        for (int i = 0; i < settings.ManualGames.Count; i++)
        {
            var game = settings.ManualGames[i];
            if (!toEnglish.TryGetValue(GameTitle.Key(game.Title), out var english))
                continue;

            // Cleaned like the titles "Ajouter un jeu" saves (no ™, no "(PC)").
            var renamed = game with { Title = GameTitle.Clean(english) };
            // Without a Steam app id, the id is the title: the player's choices
            // on the game (finished, counted in the taste) follow it. Unless
            // the same game was added twice, in both languages: left as is.
            if (renamed.Id != game.Id)
            {
                if (settings.ManualGames.Any(g => g.Id == renamed.Id))
                    continue;
                RenameKey(settings, PlayedGame.KeyOf(ManualGames.PlatformName, game.Id), PlayedGame.KeyOf(ManualGames.PlatformName, renamed.Id));
            }
            settings.ManualGames[i] = renamed;
            changed = true;
        }
        return changed;
    }

    private static void RenameKey(AppSettings settings, string oldKey, string newKey)
    {
        foreach (var keys in new[] { settings.ExcludedFromTaste, settings.IncludedInTaste, settings.MarkedCompleted })
        {
            if (keys.Remove(oldKey))
                keys.Add(newKey);
        }
    }
}
