using System.Text.Json.Serialization;

namespace GameRadar.Core.PlayStation;

/// <summary>
/// Every type the PlayStation Network, the PlayStation Store and the PS Plus
/// lists JSON is read into or written from (and the compact trophy cache),
/// generated at build time: no reflection, so the exe can be trimmed. Reading
/// is case-insensitive, as the Store and the PS Plus lists need.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
// Sign-in, trophies and played games
[JsonSerializable(typeof(SavedPlayStationAccount))]
[JsonSerializable(typeof(PlayStationTokenResponse))]
[JsonSerializable(typeof(TrophyTitlesPageDto))]
[JsonSerializable(typeof(GamesPageDto))]
[JsonSerializable(typeof(TitleTrophySetsDto))]
[JsonSerializable(typeof(PlayStationHistoryDto))]
// PlayStation Store search, best sellers and PS Plus catalog
[JsonSerializable(typeof(StoreSearchEnvelope))]
[JsonSerializable(typeof(StoreProductEnvelope))]
[JsonSerializable(typeof(StoreGridEnvelope))]
[JsonSerializable(typeof(List<PsPlusBlockDto>))]
// Persisted-query parameters sent to the Store
[JsonSerializable(typeof(StoreSearchVariables))]
[JsonSerializable(typeof(StoreProductVariables))]
[JsonSerializable(typeof(StoreGridVariables))]
[JsonSerializable(typeof(PersistedQueryExtensions))]
// Compact cache
[JsonSerializable(typeof(Dictionary<string, PlayStationHistorySource.TrophySetEntry>))]
internal sealed partial class PlayStationJsonContext : JsonSerializerContext;
