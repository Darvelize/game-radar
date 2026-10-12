using System.Text.Json;
using GameRadar.Core.Models;

namespace GameRadar.Core.Tests;

/// <summary>The JSON of a compact catalog cache, as the app writes it (reflection is off in the test host, as in the exe).</summary>
internal static class TestJson
{
    public static string Serialize(GameCatalog catalog) => JsonSerializer.Serialize(catalog, TestJsonContext.Default.GameCatalog);
}
