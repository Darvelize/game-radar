namespace GameRadar.Core.Models;

/// <summary>
/// A played game with what the stores tell about it: genres (main one
/// first), play modes and, when found on Steam, its user tags. Input of the
/// taste profile and of the similarity search.
/// </summary>
/// <param name="Tags">Steam user tags (tag id → relative vote weight), or null when unknown.</param>
public sealed record PlayedGameWithGenres(
    PlayedGame Game,
    IReadOnlyList<string> Genres,
    PlayModes Modes = PlayModes.None,
    IReadOnlyDictionary<int, double>? Tags = null);
