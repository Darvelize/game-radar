using GameRadar.Core.Cache;

namespace GameRadar.Core.GOG;

/// <summary>The GOG store candidates (the raw answers weigh ~3.7 MB, kept compact).</summary>
public static class GogCatalogSource
{
    // Version suffix: bumped when the translation (genres, filters) changes,
    // so that the old cache is refetched rather than read as is.
    internal const string CacheKey = "gog-catalog-fr-v1";

    public static StoreCatalogSource Create(CacheStore cache) =>
        new(cache, CacheKey, () => new GogException(CoreStrings.Gog_EmptyCatalog));
}
