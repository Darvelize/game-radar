using GameRadar.Core.Cache;
using GameRadar.Core.Resources;

namespace GameRadar.Core.PlayStation;

/// <summary>The PlayStation Store candidates: its best sellers and their pages.</summary>
public static class PlayStationStoreCatalogSource
{
    // Version suffix: bumped when the translation (genres, filters) changes,
    // so that the old cache is refetched rather than read as is.
    internal const string CacheKey = "psstore-catalog-fr-v1";

    public static StoreCatalogSource Create(CacheStore cache) =>
        new(cache, CacheKey, () => new PlayStationException(CoreStrings.PsStore_EmptyCatalog));
}
