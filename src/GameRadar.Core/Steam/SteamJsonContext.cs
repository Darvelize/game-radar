using System.Text.Json.Serialization;
using GameRadar.Core.Services;

namespace GameRadar.Core.Steam;

/// <summary>
/// Every type Steam's JSON is read into or written from (the DTOs, the
/// queries and the compact caches), generated at build time: no reflection,
/// so the exe can be trimmed. A type used with JsonSerializer and missing from
/// this list throws at its first use (the tests run with reflection off).
/// </summary>
// Web API and store answers
// The saved key and profile (encrypted by SecretStore)
[JsonSerializable(typeof(SavedSteamAccount))]
[JsonSerializable(typeof(ResolveVanityEnvelope))]
[JsonSerializable(typeof(PlayerSummariesEnvelope))]
[JsonSerializable(typeof(OwnedGamesEnvelope))]
[JsonSerializable(typeof(PlayerAchievementsEnvelope))]
[JsonSerializable(typeof(WishlistEnvelope))]
[JsonSerializable(typeof(SteamCatalogClient.TagListEnvelope))]
[JsonSerializable(typeof(SteamCatalogClient.QueryEnvelope))]
[JsonSerializable(typeof(Dictionary<string, SteamStoreClient.AppDetailsDto>))]
[JsonSerializable(typeof(SteamStoreClient.AppDataDto))]
[JsonSerializable(typeof(SteamTagClient.ItemsEnvelope))]
// Store queries sent as input_json
[JsonSerializable(typeof(CatalogQueryRequest))]
[JsonSerializable(typeof(GetItemsRequest))]
[JsonSerializable(typeof(SearchRequest))]
// Compact caches
[JsonSerializable(typeof(Dictionary<int, string>))]
[JsonSerializable(typeof(IReadOnlyDictionary<int, string>))]
[JsonSerializable(typeof(Dictionary<string, TagEntry>))]
[JsonSerializable(typeof(Dictionary<string, SteamGenreSource.GenreEntry>))]
[JsonSerializable(typeof(Dictionary<string, AchievementEntry>))]
internal sealed partial class SteamJsonContext : JsonSerializerContext;
