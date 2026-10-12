using System.Text.Json.Serialization;

namespace GameRadar.Core.GamePass;

/// <summary>
/// Every type the Microsoft Store and Game Pass JSON is read into (and the
/// compact genre cache), generated at build time: no reflection, so the exe
/// can be trimmed. The options are the ones of the client: the Store uses
/// PascalCase ("ProductId"), the lists camelCase ("id"), and some counters
/// are sent as strings ("0").
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(AutosuggestResponseDto))]
[JsonSerializable(typeof(ProductsResponseDto))]
[JsonSerializable(typeof(List<SiglEntryDto>))]
[JsonSerializable(typeof(Dictionary<string, StoreGenreSource.GenreEntry>))]
internal sealed partial class GamePassJsonContext : JsonSerializerContext;
