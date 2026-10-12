using System.Text.RegularExpressions;
using GameRadar.Core.Epic;
using GameRadar.Core.GOG;
using GameRadar.Core.GamePass;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;

namespace GameRadar.Core.Cache;

/// <summary>
/// Removes the cache files of a former format. A format change raises the
/// version suffix of a cache key ("gamepass-catalog-fr-v5" → "-v6") and the
/// old file used to stay on disk forever.
/// </summary>
public static partial class CacheCleanup
{
    // "<family>-v<number>": the family is what stays the same from one version to the next.
    [GeneratedRegex(@"^(?<family>.+)-v(?<version>\d+)$")]
    private static partial Regex Versioned();

    /// <summary>
    /// The cleanup of the app: the active keys are the ones the sources read
    /// today (each tells its own, so a version bump follows by itself), the
    /// protected ones are the former French catalogs the English titles
    /// migration still reads. A new cache with a version suffix is added here.
    /// </summary>
    public static void RemoveObsolete(CacheStore cache) =>
        RemoveObsolete(cache, ActiveKeys(cache), [EnglishTitlesMigration.FrenchGamePassKey, EnglishTitlesMigration.FrenchSteamKey]);

    /// <summary>The versioned keys the sources read today.</summary>
    internal static IReadOnlyCollection<string> ActiveKeys(CacheStore cache) =>
    [
        new GamePassCatalogSource(cache).CacheKey,
        new StoreGenreSource(cache).CacheKey,
        new SteamGenreSource(cache).CacheKey,
        new PsPlusCatalogSource(cache).CacheKey,
        SteamCatalogSource.CacheKey,
        SteamTagSource.CacheKey,
        GogCatalogSource.CacheKey,
        EpicCatalogSource.CacheKey,
        PlayStationStoreCatalogSource.CacheKey,
    ];

    /// <summary>
    /// Deletes every file whose family is one of the active keys' but whose
    /// version is not the active one. Anything else stays: files of an unknown
    /// family (history, library, a future cache), keys without a version,
    /// and the protected keys, still read by a migration that deletes them
    /// itself. Never throws: a locked file is left for the next launch.
    /// </summary>
    public static void RemoveObsolete(CacheStore cache, IReadOnlyCollection<string> activeKeys, IReadOnlyCollection<string> protectedKeys)
    {
        // Family → the one key read today.
        var active = new Dictionary<string, string>();
        foreach (var key in activeKeys)
        {
            if (Versioned().Match(key) is { Success: true } match)
                active[match.Groups["family"].Value] = key;
        }

        foreach (var key in cache.Keys())
        {
            if (Versioned().Match(key) is { Success: true } match
                && active.TryGetValue(match.Groups["family"].Value, out var current)
                && key != current
                && !protectedKeys.Contains(key))
            {
                cache.Delete(key);
            }
        }
    }
}
