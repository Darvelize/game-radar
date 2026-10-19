using System.Text.Json.Serialization;

namespace GameRadar.Core.GamePass;

// Raw JSON shapes of the two unofficial Microsoft endpoints used for the
// Game Pass catalog. Internal on purpose, like the Xbox DTOs: only this
// folder changes if Microsoft changes the format.
// Every property is nullable because these APIs often omit fields.

// catalog.gamepass.com/sigls/v2 returns an array whose FIRST element is a
// header describing the list ({ siglId, title, ... }); every following
// element is a product: { "id": "9NPDN9R45JX4" }.
internal sealed class SiglEntryDto
{
    public string? Id { get; set; }
    public string? SiglId { get; set; }
}

// displaycatalog.mp.microsoft.com/v7.0/products
internal sealed class ProductsResponseDto
{
    public List<ProductDto>? Products { get; set; }
}

internal sealed class ProductDto
{
    public string? ProductId { get; set; }
    public List<LocalizedPropertiesDto>? LocalizedProperties { get; set; }
    public ProductPropertiesDto? Properties { get; set; }

    // Contains { IdType: "XboxTitleId", Value: "1643147108" }: the same id
    // as Xbox Live's titleId, the most reliable way to match a played game.
    public List<AlternateIdDto>? AlternateIds { get; set; }

    public List<MarketPropertiesDto>? MarketProperties { get; set; }
}

internal sealed class MarketPropertiesDto
{
    // Community ratings over several periods: "7Days", "30Days", "AllTime".
    public List<UsageDataDto>? UsageData { get; set; }
}

internal sealed class UsageDataDto
{
    public string? AggregateTimeSpan { get; set; }
    public double? AverageRating { get; set; }
    public int? RatingCount { get; set; }
}

internal sealed class LocalizedPropertiesDto
{
    public string? ProductTitle { get; set; }
    public List<ImageDto>? Images { get; set; }
}

// Several pictures per product, told apart by their purpose:
// "TitledHeroArt" (16:9 with the title), "SuperHeroArt" (16:9, no title),
// "BoxArt" (square), "Poster", "Screenshot", "Logo"...
// Uri has no scheme: "//store-images.s-microsoft.com/image/...".
internal sealed class ImageDto
{
    public string? ImagePurpose { get; set; }
    public string? Uri { get; set; }
}

internal sealed class ProductPropertiesDto
{
    // Genres are in English whatever the requested language
    // ("Action & adventure", "Shooter"...): fine as internal keys.
    public string? Category { get; set; }
    public List<string>? Categories { get; set; }
    public string? PackageFamilyName { get; set; }

    // Mixes play modes ("SinglePlayer", "XblOnlineCoop"...) with technical
    // capabilities ("Capability4k", "XblCloudSaves"...).
    public List<AttributeDto>? Attributes { get; set; }
}

internal sealed class AttributeDto
{
    public string? Name { get; set; }
}

internal sealed class AlternateIdDto
{
    public string? IdType { get; set; }
    public string? Value { get; set; }
}

// Store search as you type ("autosuggest"): a few products per family.
internal sealed class AutosuggestResponseDto
{
    public List<AutosuggestResultDto>? Results { get; set; }
}

internal sealed class AutosuggestResultDto
{
    public List<AutosuggestProductDto>? Products { get; set; }
}

internal sealed class AutosuggestProductDto
{
    public string? ProductId { get; set; }
    // "Game" for games; add-ons are "Durable", bundles other values.
    public string? Type { get; set; }
    public string? Title { get; set; }
}

// emerald.xboxservices.com/xboxcomfd/browse: the channel xbox.com's Free Play
// Days page lists (POST body, then the answer: the channel's product ids in
// order, and a summary per product).
internal sealed class BrowseRequestDto
{
    [JsonPropertyName("Filters")]
    public string Filters { get; set; } = "";

    [JsonPropertyName("ReturnFilters")]
    public bool ReturnFilters { get; set; }

    [JsonPropertyName("ChannelKeyToBeUsedInResponse")]
    public string? ChannelKeyToBeUsedInResponse { get; set; }

    [JsonPropertyName("ChannelId")]
    public string? ChannelId { get; set; }
}

internal sealed class BrowseResponseDto
{
    public Dictionary<string, BrowseChannelDto>? Channels { get; set; }
    public List<BrowseProductDto>? ProductSummaries { get; set; }
}

internal sealed class BrowseChannelDto
{
    public List<BrowseProductDto>? Products { get; set; }
}

internal sealed class BrowseProductDto
{
    public string? ProductId { get; set; }
    public string? Title { get; set; }

    // "Game", or "PASS" for the Game Pass plans the page shows next to the games.
    public string? ProductKind { get; set; }
    public BrowseImagesDto? Images { get; set; }
}

internal sealed class BrowseImagesDto
{
    public BrowseImageDto? SuperHeroArt { get; set; }
    public BrowseImageDto? BoxArt { get; set; }
}

internal sealed class BrowseImageDto
{
    public string? Url { get; set; }
}
