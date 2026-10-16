namespace GameRadar.Core.Models;

/// <summary>
/// PlayStation Plus plans. [Flags]: a catalog game belongs to several plans (Extra ⊂
/// Premium, measured on the French lists), while the player has one of them (or none).
/// </summary>
[Flags]
public enum PsPlusTier
{
    None = 0,
    /// <summary>The Game Catalog (Ubisoft+ Classics included).</summary>
    Extra = 1,
    /// <summary>Everything in Extra plus the Classics Catalog.</summary>
    Premium = 2,
    /// <summary>
    /// Monthly games only, no catalog: no catalog game has it, so an Essential player
    /// sees none in "Pour toi" (the games of the month are on "Jeux gratuits").
    /// </summary>
    Essential = 4
}
