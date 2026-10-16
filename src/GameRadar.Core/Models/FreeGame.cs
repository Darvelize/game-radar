namespace GameRadar.Core.Models;

/// <summary>
/// A game given away for now (Epic's games of the week, PS Plus monthly games,
/// Amazon Prime's): shown for information on the "Jeux gratuits" page, never a candidate.
/// </summary>
/// <param name="Title">Display title.</param>
/// <param name="Link">Where to get it: the store page (without language: the page adds the app's) or the claim page.</param>
/// <param name="Until">End of the offer; null when the source does not say (PS Plus).</param>
/// <param name="ImageUrl">Card picture, when the source has one.</param>
/// <param name="RedeemOn">Where the game is then redeemed ("GOG"…), when it is not where it is claimed.</param>
/// <param name="PlayOnly">Free to play until the end of the offer, not to keep (Steam's free weekends).</param>
public sealed record FreeGame(
    string Title,
    string Link,
    DateTimeOffset? Until = null,
    string? ImageUrl = null,
    string? RedeemOn = null,
    bool PlayOnly = false);

/// <summary>The free games of one source and when they were fetched.</summary>
public sealed record FreeGameList(IReadOnlyList<FreeGame> Games, DateTimeOffset FetchedAt);
