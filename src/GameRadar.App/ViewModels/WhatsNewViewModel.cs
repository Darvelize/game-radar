using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameRadar.Core.Updates;

namespace GameRadar.App.ViewModels;

// The "Nouveautés" part of the app: the release notes of CHANGELOG.md
// (embedded in the app), and a banner once after each update.
/// <param name="saver">Records the last version seen.</param>
/// <param name="showPage">Shows a page of the window (the window owns the navigation).</param>
public sealed partial class WhatsNewViewModel(SettingsSaver saver, Action<AppPage> showPage) : ObservableObject
{
    /// <summary>
    /// The first copies shared with friends (an early 1.0.0, withdrawn when the
    /// base 1.0.0 was published) did not record the last version seen: a
    /// returning user without a record comes from them.
    /// </summary>
    public static Version LastVersionWithoutRecord { get; } = new(1, 0, 0);

    /// <summary>
    /// Every released version up to the running one, newest first. Notes of a
    /// version still in preparation (newer than the app) stay hidden.
    /// </summary>
    public static IReadOnlyList<ChangelogEntry> Releases => LazyReleases.Value;

    // Lazy, not a plain static initializer: the notes are read at their first
    // use, once the language and everything else are set.
    private static readonly Lazy<IReadOnlyList<ChangelogEntry>> LazyReleases = new(LoadReleases);

    private static IReadOnlyList<ChangelogEntry> LoadReleases()
    {
        // Embedded resource (see the .csproj): readable offline, and inside
        // the single-file exe like everything else.
        using var stream = typeof(WhatsNewViewModel).Assembly.GetManifestResourceStream(Localization.ChangelogResource);
        if (stream is null)
            return [];
        using var reader = new StreamReader(stream);
        return Changelog.Parse(reader.ReadToEnd())
            .Where(e => !UpdateChecker.IsNewer(e.Version, AppInfo.Version))
            .ToList();
    }

    /// <summary>The "Nouveautés" page, versions installed since the last visit marked "Nouveau".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    public partial IReadOnlyList<ReleaseNotesRow> ReleaseNotes { get; set; } =
        Releases.Select(e => new ReleaseNotesRow(e, isNew: false)).ToList();

    /// <summary>False for a build older than every note (e.g. run with -p:Version=0.9.0): the page says so instead of staying blank.</summary>
    public bool HasReleaseNotes => ReleaseNotes.Count > 0;

    public string NoReleaseNotesLabel => Localization.Format(Strings.News_None, AppInfo.Version.ToString(3));

    /// <summary>Shown once, at the first launch of a new version.</summary>
    [ObservableProperty]
    public partial bool HasWhatsNew { get; set; }

    public string WhatsNewLabel => Localization.Format(Strings.News_Banner, AppInfo.Version.ToString(3));

    [RelayCommand]
    private void Open()
    {
        HasWhatsNew = false;
        showPage(AppPage.WhatsNew);
    }

    [RelayCommand]
    private void Dismiss() => HasWhatsNew = false;

    /// <summary>
    /// Announces the news of the versions installed since the last launch,
    /// then records the running version so the banner shows only once.
    /// A first launch shows nothing: there is no "before" to compare with.
    /// </summary>
    /// <param name="returningUser">GameRadar was used before on this computer (saved settings or history).</param>
    public async Task CheckAsync(bool returningUser)
    {
        var settings = saver.Settings;
        string current = AppInfo.Version.ToString(3);
        if (settings.LastSeenVersion == current)
            return;

        Version? lastSeen = Version.TryParse(settings.LastSeenVersion, out var v) ? v : null;
        if (lastSeen is not null || returningUser)
        {
            var unseen = Changelog.Unseen(Releases, lastSeen ?? LastVersionWithoutRecord, AppInfo.Version);
            if (unseen.Count > 0)
            {
                ReleaseNotes = Releases.Select(e => new ReleaseNotesRow(e, unseen.Contains(e))).ToList();
                HasWhatsNew = true;
            }
        }

        settings.LastSeenVersion = current;
        try
        {
            await saver.SaveAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth a message: at worst, the banner comes back next time.
        }
    }
}
