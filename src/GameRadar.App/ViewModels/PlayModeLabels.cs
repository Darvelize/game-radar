using GameRadar.Core.Models;

namespace GameRadar.App.ViewModels;

/// <summary>Display names of the play modes, in the language of the app (like genre names).</summary>
public static class PlayModeLabels
{
    private static readonly (PlayModes Mode, string Key)[] Keys =
    [
        (PlayModes.Solo, nameof(Strings.Mode_Solo)),
        (PlayModes.CoopOnline, nameof(Strings.Mode_CoopOnline)),
        (PlayModes.MultiOnline, nameof(Strings.Mode_MultiOnline)),
        (PlayModes.Local, nameof(Strings.Mode_Local))
    ];

    /// <summary>Every mode of a combined value, e.g. Solo | CoopOnline -> ["solo", "coop en ligne"].</summary>
    public static IEnumerable<string> Display(PlayModes modes) =>
        Keys.Where(m => modes.HasFlag(m.Mode)).Select(m => Strings.ResourceManager.GetString(m.Key, Strings.Culture)!);
}
