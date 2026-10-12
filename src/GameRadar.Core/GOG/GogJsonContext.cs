using System.Text.Json.Serialization;

namespace GameRadar.Core.GOG;

/// <summary>
/// Every type GOG's JSON is read into, generated at build time: no
/// reflection, so the exe can be trimmed. A type used with JsonSerializer and
/// missing from this list throws at its first use (the tests run with reflection off).
/// </summary>
[JsonSerializable(typeof(GogCatalogClient.CatalogPageDto))]
[JsonSerializable(typeof(GogClient.ProfilePageDto))]
internal sealed partial class GogJsonContext : JsonSerializerContext;
