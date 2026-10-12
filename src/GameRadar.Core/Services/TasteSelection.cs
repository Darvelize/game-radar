using GameRadar.Core.Models;

namespace GameRadar.Core.Services;

/// <summary>
/// Which played games count in the taste profile: the player's own choice
/// ("Compte dans mes goûts") first, else an automatic rule. The rule leaves
/// out barely played games (<see cref="TasteProfile.IsBarelyPlayed"/>) and
/// takes them in as soon as the player gets further: nothing to re-tick.
/// </summary>
public static class TasteSelection
{
    /// <summary>
    /// The automatic rule, without any choice of the player. Give the game
    /// with its manual "finished" mark applied (<see cref="PlayStatusOverrides"/>):
    /// a game marked finished always counts.
    /// </summary>
    public static bool CountsByDefault(PlayedGame game) =>
        game.HasEngagementSignal && !TasteProfile.IsBarelyPlayed(game);

    /// <param name="excludedKeys">Games the player unticked (<see cref="PlayedGame.Key"/>).</param>
    /// <param name="includedKeys">Games the player ticked although the rule leaves them out.</param>
    public static bool Counts(PlayedGame game, IReadOnlySet<string> excludedKeys, IReadOnlySet<string> includedKeys) =>
        // The player's choice first, else the rule itself (one place to change it).
        game.HasEngagementSignal
        && (includedKeys.Contains(game.Key) || (!excludedKeys.Contains(game.Key) && CountsByDefault(game)));
}
