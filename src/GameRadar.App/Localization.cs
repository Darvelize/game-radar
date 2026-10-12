using System.Globalization;

namespace GameRadar.App;

/// <summary>
/// The language of the interface, chosen once at startup (changing it asks
/// for a restart: every text is read once, when its view is built).
/// French or English: the user's choice, otherwise the Windows language.
/// </summary>
public static class Localization
{
    public const string French = "fr";
    public const string English = "en";

    // The Windows display language, read before Apply changes anything.
    private static readonly CultureInfo WindowsCulture = CultureInfo.CurrentUICulture;

    /// <summary>Language of the running app, "fr" or "en" (set by <see cref="Apply"/>).</summary>
    public static string Current { get; private set; } = Resolve(null);

    /// <summary>
    /// "fr" or "en": the chosen language, or for "automatic" (null) the one
    /// of Windows, French for a French Windows and English for any other.
    /// </summary>
    public static string Resolve(string? setting) => Resolve(setting, WindowsCulture);

    /// <summary>The same rule for any Windows language (tested for several).</summary>
    public static string Resolve(string? setting, CultureInfo windows) => setting switch
    {
        French or English => setting,
        _ => windows.TwoLetterISOLanguageName == French ? French : English
    };

    // What follows the language of the texts (the UI culture), so that the
    // tests can check both languages with a culture switch.
    /// <summary>True when the texts are French (the UI culture), false for English.</summary>
    public static bool IsFrenchUi => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == French;

    /// <summary>Language of the Steam APIs and store pages: "french" or "english".</summary>
    public static string SteamLanguage => IsFrenchUi ? "french" : "english";

    /// <summary>
    /// Locale of the xbox.com store pages: fr-FR, or en-GB for English (the
    /// European store, closest to the French catalog GameRadar works with).
    /// </summary>
    public static string XboxStoreLocale => IsFrenchUi ? "fr-FR" : "en-GB";

    /// <summary>Locale of the store.playstation.com pages: fr-fr, or en-gb for English (same reason).</summary>
    public static string PlayStationStoreLocale => IsFrenchUi ? "fr-fr" : "en-gb";

    /// <summary>The language of the Epic store pages (store.epicgames.com/fr/p/…).</summary>
    public static string EpicStoreLocale => IsFrenchUi ? "fr" : "en-US";

    /// <summary>Release notes shown by the app: CHANGELOG.md (French) or CHANGELOG.en.md.</summary>
    public static string ChangelogResource => IsFrenchUi ? "CHANGELOG.md" : "CHANGELOG.en.md";

    /// <summary>
    /// Fills a translated format ("{0} jeux, dont {1} platinés") with values
    /// written the way of the current language (1 375 / 1,375; 4,5 / 4.5).
    /// </summary>
    public static string Format(string format, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, format, values);

    /// <summary>
    /// Sets the language of the texts (UI culture) and of the dates and
    /// numbers (culture), on this thread and on every thread created later
    /// (Task.Run formats labels too). Call before any view is built.
    /// </summary>
    public static void Apply(string? setting)
    {
        Current = Resolve(setting);

        // Same language as Windows: keep the user's own regional formats
        // (a Belgian or Canadian French Windows). Otherwise, the usual ones:
        // English with European dates (17 Sept 2026) for these European users.
        if (WindowsCulture.TwoLetterISOLanguageName == Current)
            return;
        var culture = CultureInfo.GetCultureInfo(Current == French ? "fr-FR" : "en-GB");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
