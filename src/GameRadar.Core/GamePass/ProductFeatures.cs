using GameRadar.Core.Models;

namespace GameRadar.Core.GamePass;

/// <summary>What the Store tells about any product: genres (main first) and play modes.</summary>
public sealed record ProductFeatures(IReadOnlyList<string> Genres, PlayModes Modes);
