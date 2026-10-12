using System.Text.Json.Serialization;

namespace GameRadar.Core.Settings;

/// <summary>
/// settings.json, generated at build time instead of read by reflection (the
/// exe can then be trimmed). Indented, as the file always was: a player may open it.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
