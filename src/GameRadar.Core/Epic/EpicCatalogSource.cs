using GameRadar.Core.Cache;

namespace GameRadar.Core.Epic;

/// <summary>The Epic store candidates: its collections and their ratings.</summary>
public static class EpicCatalogSource
{
    // Version suffix: bumped when the translation (genres, filters) changes,
    // so that the old cache is refetched rather than read as is.
    internal const string CacheKey = "epic-catalog-fr-v1";

    public static StoreCatalogSource Create(CacheStore cache) =>
        new(cache, CacheKey, () => new EpicException(CoreStrings.Epic_EmptyCatalog));
}
