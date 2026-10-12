using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;
using GameRadar.Core.Settings;
using GameRadar.Core.Updates;

namespace GameRadar.App.Tests;

public sealed class WhatsNewTests : AppTest
{
    private static string Current => AppInfo.Version.ToString(3);

    [Fact]
    public void The_running_version_has_release_notes()
    {
        // Guards the release: bumping <Version> without writing its notes in
        // CHANGELOG.md fails here, before anything is published.
        Assert.Contains(WhatsNewViewModel.Releases, e => e.Version.ToString(3) == Current);
    }

    [Theory]
    [InlineData("CHANGELOG.md")]
    [InlineData("CHANGELOG.en.md")]
    public void The_release_notes_of_each_language_are_in_the_app_itself(string resource)
    {
        // "CHANGELOG.en.md" once went to the en\ satellite assembly (MSBuild
        // takes ".en." for a culture): the English page had no notes. Checked
        // here directly, because Releases is read once, in the language of
        // whichever test comes first.
        Assert.Contains(resource, typeof(MainWindowViewModel).Assembly.GetManifestResourceNames());
    }

    [AvaloniaFact]
    public void The_page_lists_the_notes_or_says_there_are_none()
    {
        var vm = SampleData.Empty(TempFolder);
        Assert.True(vm.WhatsNew.HasReleaseNotes);

        // Build older than every note (run with -p:Version=0.9.0).
        vm.WhatsNew.ReleaseNotes = [];

        Assert.False(vm.WhatsNew.HasReleaseNotes);
        Assert.Contains(Current, vm.WhatsNew.NoReleaseNotesLabel);
    }

    [AvaloniaFact]
    public async Task First_launch_shows_nothing_and_remembers_the_version()
    {
        var vm = SampleData.Empty(TempFolder);

        await vm.WhatsNew.CheckAsync(returningUser: false);

        Assert.False(vm.WhatsNew.HasWhatsNew);
        Assert.Equal(Current, new SettingsStore(TempFolder).Load().LastSeenVersion);
    }

    // Older than every note: every version of the changelog is new since then.
    private const string LongAgo = "0.1.0";

    [AvaloniaFact]
    public async Task After_an_update_shows_the_banner_once()
    {
        await SaveLastSeenAsync(LongAgo);
        var vm = SampleData.Empty(TempFolder);

        await vm.WhatsNew.CheckAsync(returningUser: true);

        Assert.True(vm.WhatsNew.HasWhatsNew);
        Assert.All(vm.WhatsNew.ReleaseNotes, r => Assert.True(r.IsNew));

        // Next launch: already seen.
        var next = SampleData.Empty(TempFolder);
        await next.WhatsNew.CheckAsync(returningUser: true);
        Assert.False(next.WhatsNew.HasWhatsNew);
    }

    [AvaloniaFact]
    public async Task A_returning_user_without_record_comes_from_the_first_copies()
    {
        // The first copies shared with friends did not record the last version seen.
        var vm = SampleData.Empty(TempFolder);

        await vm.WhatsNew.CheckAsync(returningUser: true);

        bool newerThanThem = Changelog.Unseen(WhatsNewViewModel.Releases,
            WhatsNewViewModel.LastVersionWithoutRecord, AppInfo.Version).Count > 0;
        Assert.Equal(newerThanThem, vm.WhatsNew.HasWhatsNew);
    }

    [AvaloniaFact]
    public async Task The_banner_leads_to_the_page()
    {
        await SaveLastSeenAsync(LongAgo);
        var vm = SampleData.Empty(TempFolder);
        await vm.WhatsNew.CheckAsync(returningUser: true);

        vm.WhatsNew.OpenCommand.Execute(null);

        Assert.False(vm.WhatsNew.HasWhatsNew);
        Assert.True(vm.IsWhatsNewPage);
    }

    [AvaloniaTheory]
    [InlineData(AppPage.Settings)]
    [InlineData(AppPage.Recommendations)]
    [InlineData(AppPage.History)]
    public void Back_returns_to_the_page_it_was_opened_from(AppPage from)
    {
        var vm = SampleData.Empty(TempFolder);
        vm.CurrentPage = from;

        vm.NavigateCommand.Execute(AppPage.WhatsNew);
        vm.GoBackCommand.Execute(null);

        Assert.Equal(from, vm.CurrentPage);
    }

    [AvaloniaFact]
    public void Escape_does_nothing_outside_a_side_page()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.CurrentPage = AppPage.History;

        Assert.False(vm.GoBackCommand.CanExecute(null));
        vm.CurrentPage = AppPage.WhatsNew;
        Assert.True(vm.GoBackCommand.CanExecute(null));
        vm.CurrentPage = AppPage.Terms;
        Assert.True(vm.GoBackCommand.CanExecute(null));
        vm.CurrentPage = AppPage.Licenses;
        Assert.True(vm.GoBackCommand.CanExecute(null));
    }

    private async Task SaveLastSeenAsync(string version)
    {
        var store = new SettingsStore(TempFolder);
        var settings = store.Load();
        settings.LastSeenVersion = version;
        await store.SaveAsync(settings);
    }
}
