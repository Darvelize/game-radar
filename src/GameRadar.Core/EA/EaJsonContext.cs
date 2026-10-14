using System.Text.Json.Serialization;
using GameRadar.Core.Services;

namespace GameRadar.Core.EA;

/// <summary>
/// Every type EA's JSON is read into or written from, generated at build
/// time: no reflection, so the exe can be trimmed. A type used with
/// JsonSerializer and missing from this list throws at its first use (the
/// tests run with reflection off).
/// </summary>
[JsonSerializable(typeof(TokenPageDto))]
// GraphQL requests
[JsonSerializable(typeof(GraphQlRequest<NoVariables>))]
[JsonSerializable(typeof(GraphQlRequest<GameSlugsVariables>))]
[JsonSerializable(typeof(GraphQlRequest<SlugsVariables>))]
[JsonSerializable(typeof(GraphQlRequest<AchievementsVariables>))]
// GraphQL answers
[JsonSerializable(typeof(GraphQlEnvelope<MeData>))]
[JsonSerializable(typeof(GraphQlEnvelope<GameSearchData>))]
[JsonSerializable(typeof(GraphQlEnvelope<GamesData>))]
[JsonSerializable(typeof(GraphQlEnvelope<AchievementsData>))]
// Compact cache
[JsonSerializable(typeof(Dictionary<string, AchievementEntry>))]
internal sealed partial class EaJsonContext : JsonSerializerContext;
