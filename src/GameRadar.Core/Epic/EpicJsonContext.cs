using System.Text.Json.Serialization;

namespace GameRadar.Core.Epic;

/// <summary>
/// Every type Epic's JSON is read into or written from, generated at build
/// time: no reflection, so the exe can be trimmed. A type used with
/// JsonSerializer and missing from this list throws at its first use (the
/// tests run with reflection off).
/// </summary>
[JsonSerializable(typeof(CodePageDto))]
[JsonSerializable(typeof(SavedEpicAccount))]
[JsonSerializable(typeof(TokensDto))]
[JsonSerializable(typeof(LibraryPageDto))]
[JsonSerializable(typeof(List<PlaytimeDto>))]
[JsonSerializable(typeof(Dictionary<string, CatalogItemDto>))]
// GraphQL
[JsonSerializable(typeof(GraphQlRequest<SandboxVariables>))]
[JsonSerializable(typeof(GraphQlRequest<PlayerSandboxVariables>))]
[JsonSerializable(typeof(GraphQlEnvelope<AchievementData>))]
[JsonSerializable(typeof(GraphQlEnvelope<PlayerAchievementData>))]
// Store (no account)
[JsonSerializable(typeof(GraphQlRequest<CollectionVariables>))]
[JsonSerializable(typeof(GraphQlEnvelope<CollectionData>))]
[JsonSerializable(typeof(GraphQlEnvelope<RatingData>))]
// Compact caches
[JsonSerializable(typeof(Dictionary<string, EpicTitle>))]
[JsonSerializable(typeof(Dictionary<string, EpicHistorySource.AchievementEntry>))]
internal sealed partial class EpicJsonContext : JsonSerializerContext;
