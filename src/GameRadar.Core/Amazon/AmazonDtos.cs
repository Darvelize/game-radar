using System.Text.Json.Serialization;

namespace GameRadar.Core.Amazon;

// Raw JSON shapes of Luna's GraphQL (luna.amazon.com/graphql, the page that
// replaced Prime Gaming). Internal on purpose: the rest of the app only sees
// FreeGame, so if the site changes, only this folder has to change.

internal sealed class GraphQlRequest
{
    [JsonPropertyName("query")]
    public string? Query { get; set; }
}

internal sealed class GraphQlEnvelope
{
    [JsonPropertyName("data")]
    public ItemsData? Data { get; set; }
}

internal sealed class ItemsData
{
    [JsonPropertyName("items")]
    public ItemListDto? Items { get; set; }
}

internal sealed class ItemListDto
{
    [JsonPropertyName("items")]
    public List<ItemDto>? Items { get; set; }
}

internal sealed class ItemDto
{
    // "FULL_GAME", or in-game loot.
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("assets")]
    public ItemAssetsDto? Assets { get; set; }

    [JsonPropertyName("offers")]
    public List<ItemOfferDto>? Offers { get; set; }
}

internal sealed class ItemAssetsDto
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    // gaming.amazon.com/<slug>-<where to redeem>/dp/<id>: the page to claim it.
    [JsonPropertyName("externalClaimLink")]
    public string? ExternalClaimLink { get; set; }

    [JsonPropertyName("cardMedia")]
    public CardMediaDto? CardMedia { get; set; }
}

internal sealed class CardMediaDto
{
    [JsonPropertyName("defaultMedia")]
    public MediaDto? DefaultMedia { get; set; }
}

internal sealed class MediaDto
{
    // A 256 px WebP: "…/I/<id>._SX256_._AGmerlin_._FMwebp_.jpg".
    [JsonPropertyName("src1x")]
    public string? Src1x { get; set; }
}

internal sealed class ItemOfferDto
{
    [JsonPropertyName("startTime")]
    public DateTimeOffset? StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTimeOffset? EndTime { get; set; }
}
