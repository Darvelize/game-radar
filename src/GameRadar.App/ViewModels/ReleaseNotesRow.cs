using GameRadar.Core.Updates;

namespace GameRadar.App.ViewModels;

/// <summary>One version on the "Nouveautés" page, labels ready to display.</summary>
public sealed class ReleaseNotesRow
{
    public ReleaseNotesRow(ChangelogEntry entry, bool isNew)
    {
        Title = Localization.Format(Strings.News_Version, entry.Version.ToString(3));
        // Same date format as the history ("17 sept. 2026"), in the language of the app.
        DateLabel = entry.Date is { } date ? Localization.Format(Strings.News_PublishedOn, date) : Strings.News_InPreparation;
        Sections = entry.Sections.Select(s => new ReleaseNotesSectionRow(s)).ToList();
        IsNew = isNew;
    }

    public string Title { get; }

    public string DateLabel { get; }

    /// <summary>"Évolutions", "Corrections"; a single untitled part for the older versions.</summary>
    public IReadOnlyList<ReleaseNotesSectionRow> Sections { get; }

    /// <summary>Installed since the user last looked: shown with a "Nouveau" badge.</summary>
    public bool IsNew { get; }
}

/// <summary>A part of a version's notes, its title written in the changelog's own language.</summary>
public sealed class ReleaseNotesSectionRow(ChangelogSection section)
{
    public string Title { get; } = section.Title ?? string.Empty;

    /// <summary>The older versions have a single part, without a title.</summary>
    public bool HasTitle => Title.Length > 0;

    public IReadOnlyList<string> Items { get; } = section.Items;
}
