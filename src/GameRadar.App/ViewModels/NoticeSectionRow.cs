using CommunityToolkit.Mvvm.ComponentModel;

namespace GameRadar.App.ViewModels;

/// <summary>
/// A section of the open-source licenses page (a license text, or the notices
/// of what a component bundles), folded until clicked: some weigh 140 KB.
/// </summary>
public sealed partial class NoticeSectionRow : ObservableObject
{
    public NoticeSectionRow(NoticeSection section)
    {
        Title = Label(section.Title);
        Paragraphs = section.Paragraphs;
    }

    public string Title { get; }

    public IReadOnlyList<LegalParagraph> Paragraphs { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    // The titles of the file are English (tools/Update-ThirdPartyNotices.ps1):
    // "MIT License", "License of X 1.2", "Third-party notices bundled with
    // X 1.2, Y 3.4". Shown in the language of the app, without the versions
    // (they are in the list above).
    public static string Label(string title)
    {
        const string licenseOf = "License of ";
        const string bundledWith = "Third-party notices bundled with ";

        if (title.EndsWith(" License", StringComparison.Ordinal))
            return Localization.Format(Strings.Licenses_LicenseText, title[..^" License".Length]);
        if (title.StartsWith(licenseOf, StringComparison.Ordinal))
            return Localization.Format(Strings.Licenses_LicenseText, WithoutVersion(title[licenseOf.Length..]));
        if (title.StartsWith(bundledWith, StringComparison.Ordinal))
            return Localization.Format(Strings.Licenses_Bundled,
                string.Join(", ", title[bundledWith.Length..].Split(", ").Select(WithoutVersion)));
        return title;
    }

    private static string WithoutVersion(string nameAndVersion)
    {
        int space = nameAndVersion.LastIndexOf(' ');
        return space > 0 ? nameAndVersion[..space] : nameAndVersion;
    }
}
