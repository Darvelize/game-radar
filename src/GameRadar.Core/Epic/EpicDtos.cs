using System.Text.Json.Serialization;

namespace GameRadar.Core.Epic;

// The JSON of Epic's account, library, catalog and store GraphQL services
// (unofficial: every field nullable). Internal: a change of Epic's API only
// touches this folder.

/// <summary>What epicgames.com/id/api/redirect shows once signed in.</summary>
internal sealed class CodePageDto
{
    [JsonPropertyName("authorizationCode")]
    public string? AuthorizationCode { get; set; }
}

/// <summary>The answer of the token endpoint (code exchanged, or session renewed).</summary>
internal sealed class TokensDto
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    // Seconds.
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    // An error answer: errors.com.epicgames.account.oauth.authorization_code_not_found…
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }
}

internal sealed class LibraryPageDto
{
    [JsonPropertyName("records")]
    public List<LibraryRecordDto>? Records { get; set; }

    [JsonPropertyName("responseMetadata")]
    public ResponseMetadataDto? ResponseMetadata { get; set; }
}

internal sealed class ResponseMetadataDto
{
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

internal sealed class LibraryRecordDto
{
    // The id the play time is given for (its artifactId).
    [JsonPropertyName("appName")]
    public string? AppName { get; set; }

    // The game's sandbox: its achievements are asked by it.
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    // Its catalog entry: title and categories.
    [JsonPropertyName("catalogItemId")]
    public string? CatalogItemId { get; set; }
}

internal sealed class PlaytimeDto
{
    [JsonPropertyName("artifactId")]
    public string? ArtifactId { get; set; }

    // Seconds.
    [JsonPropertyName("totalTime")]
    public long? TotalTime { get; set; }
}

internal sealed class CatalogItemDto
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("categories")]
    public List<CategoryDto>? Categories { get; set; }
}

internal sealed class CategoryDto
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

/// <summary>A GraphQL request: the query and its variables.</summary>
internal sealed class GraphQlRequest<TVariables>
{
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    [JsonPropertyName("variables")]
    public required TVariables Variables { get; init; }
}

internal sealed class SandboxVariables
{
    [JsonPropertyName("sandboxId")]
    public required string SandboxId { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "en-US";
}

internal sealed class PlayerSandboxVariables
{
    [JsonPropertyName("epicAccountId")]
    public required string EpicAccountId { get; init; }

    [JsonPropertyName("sandboxId")]
    public required string SandboxId { get; init; }
}

/// <summary>A GraphQL answer: its data, and its errors when the query failed.</summary>
internal sealed class GraphQlEnvelope<TData>
{
    [JsonPropertyName("data")]
    public TData? Data { get; set; }

    // Set when the query failed, even in part.
    [JsonPropertyName("errors")]
    public List<GraphQlErrorDto>? Errors { get; set; }
}

internal sealed class AchievementData
{
    [JsonPropertyName("Achievement")]
    public AchievementQueryDto? Achievement { get; set; }
}

internal sealed class AchievementQueryDto
{
    [JsonPropertyName("productAchievementsRecordBySandbox")]
    public ProductAchievementsDto? Product { get; set; }
}

internal sealed class ProductAchievementsDto
{
    // Null: the game has no Epic achievements (the system dates from 2022).
    [JsonPropertyName("totalAchievements")]
    public int? TotalAchievements { get; set; }
}

internal sealed class PlayerAchievementData
{
    [JsonPropertyName("PlayerAchievement")]
    public PlayerAchievementQueryDto? PlayerAchievement { get; set; }
}

internal sealed class PlayerAchievementQueryDto
{
    [JsonPropertyName("playerAchievementGameRecordsBySandbox")]
    public PlayerRecordsDto? GameRecords { get; set; }
}

internal sealed class PlayerRecordsDto
{
    // Null: the player never unlocked any.
    [JsonPropertyName("records")]
    public List<PlayerRecordDto>? Records { get; set; }
}

internal sealed class PlayerRecordDto
{
    [JsonPropertyName("totalUnlocked")]
    public int? TotalUnlocked { get; set; }
}

// Only its presence matters (see EpicClient.Data).
internal sealed class GraphQlErrorDto;

// ---------- The store (candidates of "Pour toi"): collections and ratings ----------

internal sealed class CollectionVariables
{
    [JsonPropertyName("slug")]
    public required string Slug { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "en-US";
}

internal sealed class CollectionData
{
    [JsonPropertyName("Storefront")]
    public StorefrontDto? Storefront { get; set; }
}

internal sealed class StorefrontDto
{
    [JsonPropertyName("collectionLayout")]
    public CollectionLayoutDto? CollectionLayout { get; set; }
}

internal sealed class CollectionLayoutDto
{
    [JsonPropertyName("collectionOffers")]
    public List<OfferDto>? CollectionOffers { get; set; }
}

internal sealed class OfferDto
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    // The game's sandbox: its rating is asked by it.
    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("categories")]
    public List<CategoryDto>? Categories { get; set; }

    [JsonPropertyName("tags")]
    public List<TagDto>? Tags { get; set; }

    [JsonPropertyName("keyImages")]
    public List<KeyImageDto>? KeyImages { get; set; }

    [JsonPropertyName("catalogNs")]
    public CatalogNsDto? CatalogNs { get; set; }
}

internal sealed class TagDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // "genre", "feature", "platform", "usersay"...
    [JsonPropertyName("groupName")]
    public string? GroupName { get; set; }
}

internal sealed class KeyImageDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

internal sealed class CatalogNsDto
{
    [JsonPropertyName("mappings")]
    public List<PageMappingDto>? Mappings { get; set; }
}

internal sealed class PageMappingDto
{
    // The game's store page: store.epicgames.com/<lang>/p/<pageSlug>.
    [JsonPropertyName("pageSlug")]
    public string? PageSlug { get; set; }
}

internal sealed class RatingData
{
    [JsonPropertyName("RatingsPolls")]
    public RatingsPollsDto? RatingsPolls { get; set; }
}

internal sealed class RatingsPollsDto
{
    [JsonPropertyName("getProductResult")]
    public ProductResultDto? ProductResult { get; set; }
}

internal sealed class ProductResultDto
{
    // Out of 5.
    [JsonPropertyName("averageRating")]
    public double? AverageRating { get; set; }
}
