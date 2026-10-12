namespace GameRadar.App.ViewModels;

/// <summary>
/// The pages of the app, in the order of the window's TabbedPage: the
/// sidebar reaches the first three; the others are side pages, left with
/// "Retour" (or Escape) to the page they were opened from.
/// </summary>
public enum AppPage
{
    Recommendations,
    History,
    Settings,

    /// <summary>Release notes: from the banner after an update, or from "À propos".</summary>
    WhatsNew,

    /// <summary>Terms of use, from "À propos": one tab per language.</summary>
    Terms,

    /// <summary>Open-source licenses, from "À propos".</summary>
    Licenses,

    /// <summary>Add a game played elsewhere, from the history or the welcome screen.</summary>
    AddGame
}
