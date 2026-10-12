using GameRadar.Core.Cache;
using GameRadar.Core.Settings;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Settings;

public class DataFolderMigrationTests : IDisposable
{
    private readonly TempFolder _root = new();

    // %APPDATA%\GameRadar of 1.0.0 / 1.1.0, and the new %LOCALAPPDATA%\GameRadar.
    private string Legacy => Path.Combine(_root.Path, "Roaming", "GameRadar");
    private DataFolder Target => new(Path.Combine(_root.Path, "Local", "GameRadar"));

    public void Dispose() => _root.Dispose();

    private void WriteLegacy(string relativePath, string content, DateTime? writtenAt = null)
    {
        string path = Path.Combine(Legacy, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (writtenAt is not null)
            File.SetLastWriteTimeUtc(path, writtenAt.Value);
    }

    [Fact]
    public async Task Move_KeepsTheChoicesAndTheCaches()
    {
        // The settings of a 1.1.0 user: games marked "Histoire finie".
        var old = new AppSettings { MarkedCompleted = ["Xbox:42"] };
        await new SettingsStore(Legacy).SaveAsync(old);
        WriteLegacy(@"cache\steam-library.json", "{ \"library\": 1 }", new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc));

        var failures = DataFolderMigration.MoveLegacyData(Legacy, Target);

        Assert.Empty(failures);
        Assert.Equal(["Xbox:42"], new SettingsStore(Target.Root).Load().MarkedCompleted);
        var cached = new CacheStore(Target.Cache).TryLoad("steam-library");
        Assert.Equal("{ \"library\": 1 }", cached?.Content);
        // The write time is the fetch date of a cache: it must survive the move.
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero), cached?.SavedAt);
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void Move_KeepsTheMostRecentFileWhenBothExist()
    {
        var older = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);
        WriteLegacy(@"cache\a.json", "legacy, newer", newer);
        WriteLegacy(@"cache\b.json", "legacy, older", older);
        Directory.CreateDirectory(Target.Cache);
        File.WriteAllText(Path.Combine(Target.Cache, "a.json"), "target, older");
        File.SetLastWriteTimeUtc(Path.Combine(Target.Cache, "a.json"), older);
        File.WriteAllText(Path.Combine(Target.Cache, "b.json"), "target, newer");
        File.SetLastWriteTimeUtc(Path.Combine(Target.Cache, "b.json"), newer);

        DataFolderMigration.MoveLegacyData(Legacy, Target);

        Assert.Equal("legacy, newer", File.ReadAllText(Path.Combine(Target.Cache, "a.json")));
        Assert.Equal("target, newer", File.ReadAllText(Path.Combine(Target.Cache, "b.json")));
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public async Task Move_NeverReplacesTheCurrentSettingsEvenWithANewerOldFile()
    {
        // The current settings, with what 1.0.0 / 1.1.0 never knew.
        var current = new AppSettings { NotInterested = { ["afk journey"] = "AFK Journey" } };
        await new SettingsStore(Target.Root).SaveAsync(current);
        File.SetLastWriteTimeUtc(Path.Combine(Target.Root, "settings.json"), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        // The old exe started again afterwards: a newer file, without those fields.
        WriteLegacy("settings.json", "{ \"MarkedCompleted\": [] }", new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));

        DataFolderMigration.MoveLegacyData(Legacy, Target);

        Assert.Equal("AFK Journey", new SettingsStore(Target.Root).Load().NotInterested["afk journey"]);
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void Move_LeavesUnknownFilesAndTheirFolder()
    {
        WriteLegacy("settings.json", "{}");
        WriteLegacy("settings.json.tmp", "half saved");
        WriteLegacy("mes notes.txt", "not ours");

        DataFolderMigration.MoveLegacyData(Legacy, Target);

        Assert.True(File.Exists(Path.Combine(Target.Root, "settings.json")));
        Assert.Equal(["mes notes.txt"], Directory.GetFileSystemEntries(Legacy).Select(Path.GetFileName));
    }

    [Fact]
    public void Move_ReportsAFileThatCannotMoveAndGoesOn()
    {
        WriteLegacy(@"cache\locked.json", "in use");
        WriteLegacy(@"cache\free.json", "free");

        IReadOnlyList<(string File, Exception Error)> failures;
        // Opened without sharing, as another program would: it cannot move.
        using (new FileStream(Path.Combine(Legacy, "cache", "locked.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            failures = DataFolderMigration.MoveLegacyData(Legacy, Target);

        Assert.Equal("locked.json", Assert.Single(failures).File);
        Assert.True(File.Exists(Path.Combine(Target.Cache, "free.json")));
        // Still there: the next launch tries again.
        Assert.True(File.Exists(Path.Combine(Legacy, "cache", "locked.json")));
    }

    [Fact]
    public void Move_DoesNothingOnAFreshComputer()
    {
        var failures = DataFolderMigration.MoveLegacyData(Legacy, Target);

        Assert.Empty(failures);
        Assert.False(Directory.Exists(Target.Root));
    }
}
