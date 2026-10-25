using GameRadar.Core.Cache;

namespace GameRadar.Core.Steam;

/// <summary>The Steam store candidates (the raw answers weigh ~7 MB, kept compact).</summary>
public static class SteamCatalogSource
{
    // Version suffix: bumped when the translation (tags, filters) changes
    // (v2: tags kept for the similarity search; v3: English names),
    // so that the old cache is refetched rather than read as is.
    internal const string CacheKey = "steam-catalog-fr-v3";

    public static StoreCatalogSource Create(CacheStore cache) =>
        new(cache, CacheKey, () => new SteamException(CoreStrings.Steam_EmptyCatalog));
}
