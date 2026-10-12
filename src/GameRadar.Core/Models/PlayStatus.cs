namespace GameRadar.Core.Models;

/// <summary>
/// How far the player went in a game.
/// The numeric order matters: a higher value is a stronger signal
/// that the player enjoyed the game (used later for scoring).
/// </summary>
public enum PlayStatus
{
    /// <summary>Launched the game, but not finished nor 100%.</summary>
    Played = 0,

    /// <summary>
    /// Main story finished. Achievements alone cannot reliably tell this,
    /// so the user will set it manually (next step).
    /// </summary>
    Completed = 1,

    /// <summary>Every achievement unlocked (100%).</summary>
    Mastered = 2
}
