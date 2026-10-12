namespace GameRadar.App.ViewModels;

/// <summary>The tabs of the settings page, one category each.</summary>
public enum SettingsTab
{
    /// <summary>Xbox (with the Game Pass plan) and Steam; later other platforms.</summary>
    Accounts,

    /// <summary>How GameRadar behaves: language; later country, updates, theme.</summary>
    Preferences,

    /// <summary>What is loaded and when; later the data folder and portable mode.</summary>
    Data,

    /// <summary>Privacy statement, revoking the access, erasing everything.</summary>
    Privacy,

    /// <summary>Version, release notes, error log, sources.</summary>
    About
}
