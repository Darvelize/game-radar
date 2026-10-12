using System.Text.Json.Serialization;

namespace GameRadar.Core.Steam;

// Raw JSON shapes of the Steam Web API and of the Steam store.
// Internal on purpose: the rest of the app only sees PlayedGame, so if the
// API changes, only this folder has to change.

// ---------- ISteamUser/ResolveVanityURL: custom profile name -> SteamID ----------

internal sealed class ResolveVanityEnvelope
{
    [JsonPropertyName("response")]
    public ResolveVanityResponse? Response { get; set; }
}

internal sealed class ResolveVanityResponse
{
    // 1 = found, 42 = no match.
    [JsonPropertyName("success")]
    public int Success { get; set; }

    [JsonPropertyName("steamid")]
    public string? SteamId { get; set; }
}

// ---------- ISteamUser/GetPlayerSummaries: display name ----------

internal sealed class PlayerSummariesEnvelope
{
    [JsonPropertyName("response")]
    public PlayerSummariesResponse? Response { get; set; }
}

internal sealed class PlayerSummariesResponse
{
    [JsonPropertyName("players")]
    public List<PlayerSummaryDto>? Players { get; set; }
}

internal sealed class PlayerSummaryDto
{
    [JsonPropertyName("personaname")]
    public string? PersonaName { get; set; }
}

// ---------- IPlayerService/GetOwnedGames: the library ----------

internal sealed class OwnedGamesEnvelope
{
    [JsonPropertyName("response")]
    public OwnedGamesResponse? Response { get; set; }
}

internal sealed class OwnedGamesResponse
{
    // Missing (with "games") when the profile's game details are private;
    // 0 when the library is really empty.
    [JsonPropertyName("game_count")]
    public int? GameCount { get; set; }

    [JsonPropertyName("games")]
    public List<OwnedGameDto>? Games { get; set; }
}

internal sealed class OwnedGameDto
{
    [JsonPropertyName("appid")]
    public long AppId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // Minutes, all platforms together.
    [JsonPropertyName("playtime_forever")]
    public int PlaytimeForever { get; set; }

    // Unix time (seconds); 0 = never played.
    [JsonPropertyName("rtime_last_played")]
    public long LastPlayedUnix { get; set; }

    // False for games without achievements or stats: no need to ask for them.
    [JsonPropertyName("has_community_visible_stats")]
    public bool HasCommunityVisibleStats { get; set; }
}

// ---------- ISteamUserStats/GetPlayerAchievements: one game ----------

internal sealed class PlayerAchievementsEnvelope
{
    [JsonPropertyName("playerstats")]
    public PlayerStatsDto? PlayerStats { get; set; }
}

internal sealed class PlayerStatsDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("achievements")]
    public List<PlayerAchievementDto>? Achievements { get; set; }
}

internal sealed class PlayerAchievementDto
{
    // 1 = unlocked.
    [JsonPropertyName("achieved")]
    public int Achieved { get; set; }
}

// ---------- Steam store queries (IStoreQueryService, IStoreBrowseService) ----------

/// <summary>
/// The "context" of every store query: English names, like every other
/// source (games are matched by title); the country stays France (prices,
/// availability).
/// </summary>
internal sealed class StoreContextDto
{
    public static readonly StoreContextDto France = new();

    [JsonPropertyName("language")]
    public string Language => "english";

    [JsonPropertyName("country_code")]
    public string CountryCode => "FR";
}

/// <summary>A tag voted on a game: its id and its votes.</summary>
internal sealed class TagWeightDto
{
    [JsonPropertyName("tagid")]
    public int TagId { get; set; }

    [JsonPropertyName("weight")]
    public int Weight { get; set; }
}

// ---------- IWishlistService/GetWishlist: the wishlist ----------

internal sealed class WishlistEnvelope
{
    [JsonPropertyName("response")]
    public WishlistResponse? Response { get; set; }
}

internal sealed class WishlistResponse
{
    // Missing when the list is empty or hidden: the two look the same.
    [JsonPropertyName("items")]
    public List<WishlistItemDto>? Items { get; set; }
}

internal sealed class WishlistItemDto
{
    [JsonPropertyName("appid")]
    public int AppId { get; set; }
}

// ---------- Requests: input_json of the store queries ----------
// Named classes (not anonymous objects) so the JSON generator can write them;
// the property ORDER is the order of the JSON, the names are the API's own.

/// <summary>What the answer should carry besides ids: tags, reviews, pictures. Unset = left out.</summary>
internal sealed class DataRequestDto
{
    [JsonPropertyName("include_tag_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IncludeTagCount { get; init; }

    [JsonPropertyName("include_reviews")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeReviews { get; init; }

    [JsonPropertyName("include_assets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeAssets { get; init; }
}

/// <summary>IStoreQueryService/Query: one page of the best sellers.</summary>
internal sealed class CatalogQueryRequest
{
    [JsonPropertyName("query")]
    public required CatalogQueryDto Query { get; init; }

    [JsonPropertyName("context")]
    public StoreContextDto Context { get; init; } = StoreContextDto.France;

    [JsonPropertyName("data_request")]
    public required DataRequestDto DataRequest { get; init; }
}

internal sealed class CatalogQueryDto
{
    [JsonPropertyName("start")]
    public int Start { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("sort")]
    public int Sort { get; init; }

    [JsonPropertyName("filters")]
    public required CatalogFiltersDto Filters { get; init; }
}

internal sealed class CatalogFiltersDto
{
    [JsonPropertyName("type_filters")]
    public required TypeFiltersDto TypeFilters { get; init; }
}

internal sealed class TypeFiltersDto
{
    [JsonPropertyName("include_games")]
    public bool IncludeGames { get; init; }
}

/// <summary>IStoreBrowseService/GetItems: the tags of these apps.</summary>
internal sealed class GetItemsRequest
{
    [JsonPropertyName("ids")]
    public required List<AppIdDto> Ids { get; init; }

    [JsonPropertyName("context")]
    public StoreContextDto Context { get; init; } = StoreContextDto.France;

    [JsonPropertyName("data_request")]
    public required DataRequestDto DataRequest { get; init; }
}

internal sealed class AppIdDto
{
    [JsonPropertyName("appid")]
    public int AppId { get; init; }
}

/// <summary>IStoreQueryService/SearchSuggestions: the games matching a title.</summary>
internal sealed class SearchRequest
{
    [JsonPropertyName("search_term")]
    public required string SearchTerm { get; init; }

    [JsonPropertyName("max_results")]
    public int MaxResults { get; init; }

    [JsonPropertyName("context")]
    public StoreContextDto Context { get; init; } = StoreContextDto.France;

    [JsonPropertyName("data_request")]
    public required DataRequestDto DataRequest { get; init; }
}
