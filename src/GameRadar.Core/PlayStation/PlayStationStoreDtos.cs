using System.Text.Json.Serialization;

namespace GameRadar.Core.PlayStation;

// Answers of the PlayStation Store site (web.np.playstation.com GraphQL). Internal
// to this folder: a change of the site only touches it. Properties are nullable:
// the answer omits what it does not know.

internal sealed class StoreSearchEnvelope
{
    public StoreSearchData? Data { get; set; }
}

internal sealed class StoreSearchData
{
    public StoreUniversalSearch? UniversalSearch { get; set; }
}

internal sealed class StoreUniversalSearch
{
    // Products and concepts, mixed.
    public List<StoreSearchResult>? Results { get; set; }
}

internal sealed class StoreSearchResult
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    // FULL_GAME for a game; PREMIUM_EDITION, GAME_BUNDLE, ADD_ON_PACK, DEMO, COSTUME,
    // LEVEL, ITEM for the rest; absent on a concept.
    public string? StoreDisplayClassification { get; set; }
}

internal sealed class StoreProductEnvelope
{
    public StoreProductData? Data { get; set; }
}

internal sealed class StoreProductData
{
    public StoreProduct? ProductRetrieve { get; set; }
}

internal sealed class StoreProduct
{
    public List<StoreGenre>? LocalizedGenres { get; set; }
    public StoreStarRating? StarRating { get; set; }
}

internal sealed class StoreStarRating
{
    // Out of 5; the store gives no number of ratings.
    public double? AverageRating { get; set; }
}

internal sealed class StoreGridEnvelope
{
    public StoreGridData? Data { get; set; }
}

internal sealed class StoreGridData
{
    public StoreGrid? CategoryGridRetrieve { get; set; }
}

internal sealed class StoreGrid
{
    // In the order asked (sales30: best sellers first).
    public List<StoreGridProduct>? Products { get; set; }
}

internal sealed class StoreGridProduct
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    // FULL_GAME for a game; GAME_BUNDLE, PREMIUM_EDITION... for the rest.
    public string? StoreDisplayClassification { get; set; }
    public List<StoreMedia>? Media { get; set; }
}

internal sealed class StoreMedia
{
    // IMAGE or VIDEO.
    public string? Type { get; set; }
    // MASTER, SIXTEEN_BY_NINE_BANNER, GAMEHUB_COVER_ART, SCREENSHOT...
    public string? Role { get; set; }
    public string? Url { get; set; }
}

internal sealed class StoreGenre
{
    public string? Value { get; set; }
}

// ---------- Requests: the variables and extensions of a persisted query ----------
// Named classes (not anonymous objects) so the JSON generator can write them;
// the names are the site's own, in camelCase, and their order is the JSON's.

/// <summary>getSearchResults: what the Store search is asked.</summary>
internal sealed class StoreSearchVariables
{
    [JsonPropertyName("countryCode")]
    public required string CountryCode { get; init; }

    [JsonPropertyName("languageCode")]
    public required string LanguageCode { get; init; }

    [JsonPropertyName("nextCursor")]
    public required string NextCursor { get; init; }

    [JsonPropertyName("pageOffset")]
    public int PageOffset { get; init; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }

    [JsonPropertyName("searchTerm")]
    public required string SearchTerm { get; init; }
}

/// <summary>categoryGridRetrieve: one page of a store category, sorted and filtered.</summary>
internal sealed class StoreGridVariables
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("pageArgs")]
    public required StorePageArgs PageArgs { get; init; }

    [JsonPropertyName("sortBy")]
    public required StoreSort SortBy { get; init; }

    // "<facet>:<key>", as the site writes them: "storeDisplayClassification:FULL_GAME".
    [JsonPropertyName("filterBy")]
    public required List<string> FilterBy { get; init; }

    [JsonPropertyName("facetOptions")]
    public List<string> FacetOptions { get; init; } = [];
}

internal sealed class StorePageArgs
{
    [JsonPropertyName("size")]
    public int Size { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }
}

internal sealed class StoreSort
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("isAscending")]
    public bool IsAscending { get; init; }
}

/// <summary>The product page query: which game.</summary>
internal sealed class StoreProductVariables
{
    [JsonPropertyName("productId")]
    public required string ProductId { get; init; }
}

/// <summary>The hash of a query the site has registered ("persisted"), which is all it accepts.</summary>
internal sealed class PersistedQueryExtensions
{
    [JsonPropertyName("persistedQuery")]
    public required PersistedQueryDto PersistedQuery { get; init; }
}

internal sealed class PersistedQueryDto
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("sha256Hash")]
    public required string Sha256Hash { get; init; }
}
