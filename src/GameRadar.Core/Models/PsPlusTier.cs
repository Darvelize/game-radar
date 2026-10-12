namespace GameRadar.Core.Models;

/// <summary>
/// PlayStation Plus plans that include a catalog (Essential only gives monthly
/// games, no catalog). [Flags]: a game belongs to several plans (Extra ⊂ Premium,
/// measured on the French lists), while the player has one of them (or none).
/// </summary>
[Flags]
public enum PsPlusTier
{
    None = 0,
    /// <summary>The Game Catalog (Ubisoft+ Classics included).</summary>
    Extra = 1,
    /// <summary>Everything in Extra plus the Classics Catalog.</summary>
    Premium = 2
}
