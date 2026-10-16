using System.Text.Json;
using GameRadar.Core.Models;

namespace GameRadar.App.Tests;

/// <summary>The JSON of a compact catalog cache, as the app writes it (reflection is off in the test host, as in the exe).</summary>
internal static class TestJson
{
    public static string Serialize(GameCatalog catalog) => JsonSerializer.Serialize(catalog, CoreJsonContext.Default.GameCatalog);

    public static string Serialize(FreeGameList list) => JsonSerializer.Serialize(list, CoreJsonContext.Default.FreeGameList);
}
