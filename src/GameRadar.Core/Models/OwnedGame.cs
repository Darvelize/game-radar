namespace GameRadar.Core.Models;

/// <summary>
/// One of the player's own games, not finished yet ("Mes jeux"): where they
/// have it and how far they went, for its card.
/// </summary>
/// <param name="Platforms">The platforms of its history lines ("Steam", "Xbox"…), in the history's order.</param>
/// <param name="PlaytimeMinutes">Play time over the platforms that report it; null when none does.</param>
/// <param name="AchievementPercent">Best achievement progress (0 to 100) of the lines with achievements; null without any.</param>
/// <param name="NeverPlayed">Never launched on any platform.</param>
public sealed record OwnedGame(
    IReadOnlyList<string> Platforms,
    int? PlaytimeMinutes,
    double? AchievementPercent,
    bool NeverPlayed);
