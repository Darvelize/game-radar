using Avalonia.Headless.XUnit;
using GameRadar.App.ViewModels;
using GameRadar.Core.Updates;

namespace GameRadar.App.Tests;

public sealed class UpdateBannerTests : AppTest
{
    private static readonly AvailableUpdate NewVersion = new(new Version(1, 1, 0), UpdateChecker.DownloadPage);

    [AvaloniaFact]
    public void Hidden_until_a_newer_version_is_found()
    {
        var vm = SampleData.Empty(TempFolder);

        Assert.False(vm.HasUpdate);
        Assert.Empty(vm.UpdateLabel);
    }

    [AvaloniaFact]
    public void Names_both_versions()
    {
        var vm = SampleData.Empty(TempFolder);

        vm.AvailableUpdate = NewVersion;

        Assert.True(vm.HasUpdate);
        Assert.Equal(
            $"GameRadar 1.1.0 est disponible (tu as la {AppInfo.Version.ToString(3)}).",
            vm.UpdateLabel);
    }

    [AvaloniaFact]
    public void Download_opens_the_release_page()
    {
        Uri? opened = null;
        var vm = SampleData.Empty(TempFolder, uri => opened = uri);
        vm.AvailableUpdate = NewVersion;

        vm.OpenUpdatePageCommand.Execute(null);

        Assert.Equal(UpdateChecker.DownloadPage, opened);
    }

    [AvaloniaFact]
    public void Later_hides_the_banner()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.AvailableUpdate = NewVersion;

        vm.DismissUpdateCommand.Execute(null);

        Assert.False(vm.HasUpdate);
    }

    [Fact]
    public void App_version_comes_from_the_project()
    {
        // <Version> of the .csproj: 0.0.0 would mean the lookup failed, and
        // every release would then be announced as an update.
        Assert.True(AppInfo.Version.Major >= 1);
    }
}
