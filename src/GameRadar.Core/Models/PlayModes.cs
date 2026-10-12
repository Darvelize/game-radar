namespace GameRadar.Core.Models;

/// <summary>
/// How a game can be played. [Flags]: each value is its own bit, so one
/// variable holds any combination, e.g. <c>Solo | CoopOnline</c>.
/// </summary>
[Flags]
public enum PlayModes
{
    None = 0,
    Solo = 1,
    CoopOnline = 2,
    MultiOnline = 4,
    /// <summary>Same screen or same room: local co-op, split screen, local versus.</summary>
    Local = 8
}
