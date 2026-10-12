using System.Text.Json.Serialization;

namespace GameRadar.Core.PlayStation;

// Raw JSON shapes of the PS Plus games lists of playstation.com
// (bin/imagic/gameslist). Internal on purpose: the rest of the app only sees
// CatalogGame, so if the lists change, only this folder has to change.

// One block per initial letter ("A", "B"... "#"), the games inside.
internal sealed class PsPlusBlockDto
{
    [JsonPropertyName("games")]
    public List<PsPlusGameDto>? Games { get; set; }
}

internal sealed class PsPlusGameDto
{
    // The game ("concept"), shared by its PS4 and PS5 versions.
    [JsonPropertyName("conceptId")]
    public long? ConceptId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // The English title, whatever the locale asked.
    [JsonPropertyName("nameEn")]
    public string? NameEn { get; set; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }

    // Coarse PlayStation genres ("ACTION", "ROLE_PLAYING_GAMES"...), repeated.
    [JsonPropertyName("genre")]
    public List<string>? Genre { get; set; }
}
