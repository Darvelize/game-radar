using System.Text.Json.Serialization;

namespace GameRadar.Core.EA;

// The JSON of EA's token page and of the EA app's GraphQL API (unofficial:
// every field nullable). Internal: a change of EA's API only touches this folder.

/// <summary>What accounts.ea.com shows once signed in (or its error when not).</summary>
internal sealed class TokenPageDto
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>A GraphQL request: the query and its variables.</summary>
internal sealed class GraphQlRequest<TVariables>
{
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    [JsonPropertyName("variables")]
    public TVariables? Variables { get; init; }
}

internal sealed class NoVariables;

internal sealed class GameSlugsVariables
{
    [JsonPropertyName("gameSlugs")]
    public required IReadOnlyList<string> GameSlugs { get; init; }
}

internal sealed class SlugsVariables
{
    [JsonPropertyName("slugs")]
    public required IReadOnlyList<string> Slugs { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "DEFAULT";
}

internal sealed class AchievementsVariables
{
    [JsonPropertyName("offerId")]
    public required string OfferId { get; init; }

    [JsonPropertyName("playerPsd")]
    public required string PlayerPsd { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "US";
}

/// <summary>A GraphQL answer: its data, null when the query failed (an unknown field, a refused query).</summary>
internal sealed class GraphQlEnvelope<TData>
{
    [JsonPropertyName("data")]
    public TData? Data { get; set; }
}

internal sealed class ItemsDto<T>
{
    [JsonPropertyName("items")]
    public List<T>? Items { get; set; }
}

internal sealed class MeData
{
    [JsonPropertyName("me")]
    public MeDto? Me { get; set; }
}

internal sealed class MeDto
{
    [JsonPropertyName("player")]
    public PlayerDto? Player { get; set; }

    [JsonPropertyName("recentGames")]
    public ItemsDto<RecentGameDto>? RecentGames { get; set; }
}

internal sealed class PlayerDto
{
    [JsonPropertyName("psd")]
    public string? Psd { get; set; }
}

internal sealed class RecentGameDto
{
    [JsonPropertyName("gameSlug")]
    public string? GameSlug { get; set; }

    [JsonPropertyName("lastSessionEndDate")]
    public string? LastSessionEndDate { get; set; }

    [JsonPropertyName("totalPlayTimeSeconds")]
    public long? TotalPlayTimeSeconds { get; set; }
}

internal sealed class GameSearchData
{
    [JsonPropertyName("gameSearch")]
    public ItemsDto<SlugDto>? GameSearch { get; set; }
}

internal sealed class SlugDto
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

internal sealed class GamesData
{
    [JsonPropertyName("games")]
    public ItemsDto<GameDto>? Games { get; set; }
}

internal sealed class GameDto
{
    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("products")]
    public ItemsDto<ProductDto>? Products { get; set; }
}

internal sealed class ProductDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("originOfferId")]
    public string? OriginOfferId { get; set; }

    [JsonPropertyName("baseItem")]
    public BaseItemDto? BaseItem { get; set; }
}

internal sealed class BaseItemDto
{
    // "BASE_GAME", "EXTRA_CONTENT"...
    [JsonPropertyName("gameType")]
    public string? GameType { get; set; }
}

internal sealed class AchievementsData
{
    [JsonPropertyName("achievements")]
    public List<AchievementSetDto>? Achievements { get; set; }
}

internal sealed class AchievementSetDto
{
    [JsonPropertyName("achievements")]
    public List<AchievementDto>? Achievements { get; set; }
}

internal sealed class AchievementDto
{
    // Times the player earned it: 0 = locked.
    [JsonPropertyName("awardCount")]
    public int? AwardCount { get; set; }
}
