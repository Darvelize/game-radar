using GameRadar.Core.Settings;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Settings;

public class DataFolderTests : IDisposable
{
    private readonly TempFolder _root = new();

    public void Dispose() => _root.Dispose();

    private string ExeFolder => Path.Combine(_root.Path, "exe");
    private string DefaultRoot => Path.Combine(_root.Path, "Local", "GameRadar");

    [Fact]
    public void Resolve_WithoutADataFolder_UsesTheDefaultRoot()
    {
        Directory.CreateDirectory(ExeFolder);

        var (folder, refused) = DataFolder.Resolve(ExeFolder, DefaultRoot);

        Assert.Equal(DefaultRoot, folder.Root);
        Assert.False(folder.IsPortable);
        Assert.False(refused);
    }

    [Fact]
    public void Resolve_NeverCreatesTheDataFolder()
    {
        Directory.CreateDirectory(ExeFolder);

        DataFolder.Resolve(ExeFolder, DefaultRoot);

        Assert.False(Directory.Exists(Path.Combine(ExeFolder, "Data")));
    }

    [Fact]
    public void Resolve_WithADataFolderNextToTheExe_IsPortable()
    {
        string data = Path.Combine(ExeFolder, "Data");
        Directory.CreateDirectory(data);

        var (folder, refused) = DataFolder.Resolve(ExeFolder, DefaultRoot);

        Assert.Equal(data, folder.Root);
        Assert.True(folder.IsPortable);
        Assert.False(refused);
        Assert.Equal(Path.Combine(data, "cache"), folder.Cache);
        // The writing test leaves nothing behind.
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
    }

    [Fact]
    public void Resolve_WithAFileNamedData_FallsBackWithoutAMessage()
    {
        // Not a folder: nothing can be written there.
        Directory.CreateDirectory(ExeFolder);
        File.WriteAllText(Path.Combine(ExeFolder, "Data"), "not a folder");

        var (folder, refused) = DataFolder.Resolve(ExeFolder, DefaultRoot);

        Assert.Equal(DefaultRoot, folder.Root);
        Assert.False(folder.IsPortable);
        Assert.False(refused);
    }

    [Fact]
    public void Resolve_WithAReadOnlyDataFolder_FallsBackAndSaysSo()
    {
        string data = Path.Combine(ExeFolder, "Data");
        Directory.CreateDirectory(data);
        // A folder where the probe file's name is already taken by a folder:
        // creating it fails, like a folder the user may not write to.
        Directory.CreateDirectory(Path.Combine(data, DataFolder.WriteProbeName));

        var (folder, refused) = DataFolder.Resolve(ExeFolder, DefaultRoot);

        Assert.Equal(DefaultRoot, folder.Root);
        Assert.False(folder.IsPortable);
        Assert.True(refused);
    }
}
