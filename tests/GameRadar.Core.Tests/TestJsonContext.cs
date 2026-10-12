using System.Text.Json.Serialization;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Settings;

namespace GameRadar.Core.Tests;

/// <summary>
/// The JSON the tests write or read themselves (the app's own contexts are
/// generated too: reflection is off in the test host, as in the exe). Compact,
/// like the default options the tests used before.
/// </summary>
[JsonSerializable(typeof(ManualGame))]
[JsonSerializable(typeof(CatalogGame))]
[JsonSerializable(typeof(GameCatalog))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, int>))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
