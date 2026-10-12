namespace GameRadar.App.ViewModels;

/// <summary>
/// Display names of the Store genres, in the language of the app. The Core
/// keeps the English Store labels as stable keys; translating is a display
/// concern. nameof: a mistyped key is a build error, not a missing text.
/// </summary>
public static class GenreLabels
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Action & adventure"] = nameof(Strings.Genre_ActionAdventure),
        ["Shooter"] = nameof(Strings.Genre_Shooter),
        ["Role playing"] = nameof(Strings.Genre_RolePlaying),
        ["Simulation"] = nameof(Strings.Genre_Simulation),
        ["Strategy"] = nameof(Strings.Genre_Strategy),
        ["Racing & flying"] = nameof(Strings.Genre_Racing),
        ["Sports"] = nameof(Strings.Genre_Sports),
        ["Platformer"] = nameof(Strings.Genre_Platformer),
        ["Family & kids"] = nameof(Strings.Genre_Family),
        ["Fighting"] = nameof(Strings.Genre_Fighting),
        ["Card & board"] = nameof(Strings.Genre_CardBoard),
        ["Classics"] = nameof(Strings.Genre_Classics),
        ["Puzzle & trivia"] = nameof(Strings.Genre_Puzzle),
        ["Multi-player Online Battle Arena"] = nameof(Strings.Genre_Moba),
        ["Music"] = nameof(Strings.Genre_Music),
        ["Educational"] = nameof(Strings.Genre_Educational),
        ["Tools"] = nameof(Strings.Genre_Tools),
        ["Other"] = nameof(Strings.Genre_Other)
    };

    /// <summary>Unknown genres (new Store categories) are shown as they are.</summary>
    public static string Display(string genre) =>
        Keys.TryGetValue(genre, out string? key) ? Strings.ResourceManager.GetString(key, Strings.Culture) ?? genre : genre;
}
