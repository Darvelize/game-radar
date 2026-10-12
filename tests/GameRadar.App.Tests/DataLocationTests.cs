using GameRadar.App.ViewModels;
using GameRadar.Core.Settings;

namespace GameRadar.App.Tests;

public sealed class DataLocationTests : AppTest
{
    private DataFolder Portable()
    {
        string exe = Path.Combine(TempFolder, "exe");
        Directory.CreateDirectory(Path.Combine(exe, DataFolder.PortableFolderName));
        return DataFolder.Resolve(exe, Path.Combine(TempFolder, "Local")).Folder;
    }

    [Fact]
    public void The_normal_mode_says_where_the_data_are()
    {
        var location = new DataLocationViewModel(new DataFolder(Path.Combine(TempFolder, "Local")), _ => { });

        Assert.Contains(Path.Combine(TempFolder, "Local"), location.Summary);
        Assert.Contains("normal", location.Summary);
        Assert.False(location.IsPortable);
    }

    [Fact]
    public void The_portable_mode_is_named_as_such()
    {
        var data = Portable();

        var location = new DataLocationViewModel(data, _ => { });

        Assert.Contains(data.Root, location.Summary);
        Assert.Contains("portable", location.Summary);
        Assert.True(location.IsPortable);
    }

    [Fact]
    public void Opening_theTempFolder_creates_it_when_missing_and_asks_the_launcher()
    {
        // A first launch where nothing was ever saved: the button must open something.
        string root = Path.Combine(TempFolder, "Local");
        Uri? opened = null;
        var location = new DataLocationViewModel(new DataFolder(root), uri => opened = uri);

        location.OpenFolderCommand.Execute(null);

        Assert.True(Directory.Exists(root));
        Assert.NotNull(opened);
        Assert.True(opened.IsFile);
        Assert.Equal(Path.GetFullPath(root + Path.DirectorySeparatorChar), opened.LocalPath);
    }
}
