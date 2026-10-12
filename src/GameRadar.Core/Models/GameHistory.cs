namespace GameRadar.Core.Models;

/// <summary>A player's game history and when it was fetched from the platform.</summary>
/// <param name="Games">Played games, most recent first.</param>
/// <param name="FetchedAt">When the data was retrieved from the API (not when it was read from cache).</param>
/// <param name="PlatformGenres">
/// Genres the platform itself gives (Microsoft Store vocabulary), by
/// <see cref="PlayedGame.Key"/>: the last resort for a game no other source
/// knows (a PlayStation exclusive). Null when the platform gives none.
/// </param>
public sealed record GameHistory(
    IReadOnlyList<PlayedGame> Games,
    DateTimeOffset FetchedAt,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? PlatformGenres = null);
