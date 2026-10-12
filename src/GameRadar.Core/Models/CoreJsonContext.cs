using System.Text.Json.Serialization;
using GameRadar.Core.Updates;

namespace GameRadar.Core.Models;

/// <summary>
/// JSON of the models shared by every connector (the compact catalogs the
/// caches keep), generated at build time instead of read by reflection: the
/// exe can then be trimmed. A connector's own DTOs live in its folder's
/// context (SteamJsonContext...); a type written or read anywhere without
/// being listed in one fails at its first use.
/// </summary>
[JsonSerializable(typeof(GameCatalog))]
// The update check's one field, from GitHub.
[JsonSerializable(typeof(GitHubReleaseDto))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
