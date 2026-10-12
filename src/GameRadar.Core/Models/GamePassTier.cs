namespace GameRadar.Core.Models;

/// <summary>
/// Game Pass subscription plans. [Flags]: a game belongs to several plans
/// (Essential ⊂ Premium ⊂ Ultimate, checked on the French catalog), while
/// the player has one of them (or none).
/// </summary>
[Flags]
public enum GamePassTier
{
    None = 0,
    Essential = 1,
    Premium = 2,
    /// <summary>Everything, including EA Play and Ubisoft+ Classics.</summary>
    Ultimate = 4,
    /// <summary>PC only: the PC library with EA Play, without Ubisoft+ Classics.</summary>
    PcGamePass = 8
}
