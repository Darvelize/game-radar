using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GameRadar.App.ViewModels;

// The legal texts of Settings › About, each on a side page of the app (like
// the release notes), "Retour" leading back to "À propos". Read when opened,
// not at startup: the licenses weigh ~220 KB.
/// <param name="showPage">Shows a page of the window (the window owns the navigation).</param>
public sealed partial class LegalViewModel(Action<AppPage> showPage) : ObservableObject
{
    // ---------- Terms of use: one tab per language ----------

    /// <summary>Title, copyright and version lines, above the tabs.</summary>
    [ObservableProperty]
    public partial string TermsHeader { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<LegalParagraph> TermsFrench { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<LegalParagraph> TermsEnglish { get; set; } = [];

    /// <summary>The tab shown: 0 French, 1 English (SelectedIndex of the TabbedPage, two-way).</summary>
    [ObservableProperty]
    public partial int TermsTabIndex { get; set; }

    [RelayCommand]
    private void OpenTerms()
    {
        var terms = LegalDocuments.ReadTerms();
        TermsHeader = terms.Header;
        TermsFrench = terms.French;
        TermsEnglish = terms.English;
        // Opens on the language of the app.
        TermsTabIndex = Localization.IsFrenchUi ? 0 : 1;
        showPage(AppPage.Terms);
    }

    // ---------- Open-source licenses: a list, then folded sections ----------

    [ObservableProperty]
    public partial IReadOnlyList<LicensedComponent> LicensedComponents { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<NoticeSectionRow> NoticeSections { get; set; } = [];

    [RelayCommand]
    private void OpenThirdPartyNotices()
    {
        var notices = LegalDocuments.ReadThirdPartyNotices();
        LicensedComponents = notices.Components;
        // New rows each time: every section opens folded again.
        NoticeSections = notices.Sections.Select(s => new NoticeSectionRow(s)).ToList();
        showPage(AppPage.Licenses);
    }
}
