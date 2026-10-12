namespace GameRadar.Core.Settings;

/// <summary>
/// Moves the data of GameRadar 1.0.0 and 1.1.0 (settings.json and cache\ in
/// %APPDATA%\GameRadar) into the <see cref="DataFolder"/>. Runs at startup,
/// before anything is read. Only known files move; when a cache exists on
/// both sides, the most recent one wins and the other is deleted, but the
/// current settings.json is always kept (an old exe writes fewer fields).
/// </summary>
public static class DataFolderMigration
{
    /// <summary>%APPDATA%\GameRadar (Roaming), where 1.0.0 and 1.1.0 kept settings and caches.</summary>
    public static string LegacyRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameRadar");

    /// <summary>
    /// Moves what is left in <paramref name="legacyRoot"/>. Never throws: a
    /// file that cannot move (opened by another program...) stays where it
    /// is, is reported, and the next launch tries again.
    /// </summary>
    /// <returns>The files that could not move, with the reason.</returns>
    public static IReadOnlyList<(string File, Exception Error)> MoveLegacyData(string legacyRoot, DataFolder target)
    {
        var failures = new List<(string, Exception)>();
        if (!Directory.Exists(legacyRoot))
            return failures;

        try
        {
            // The settings move only to an empty place: the current ones were
            // written by this version, an old exe started again since would
            // write fewer fields (games added by hand, refusals, language):
            // newer, but poorer.
            string settings = Path.Combine(target.Root, "settings.json");
            if (File.Exists(settings))
                TryDelete(Path.Combine(legacyRoot, "settings.json"), failures);
            else
                MoveNewest(Path.Combine(legacyRoot, "settings.json"), settings, failures);

            string legacyCache = Path.Combine(legacyRoot, "cache");
            if (Directory.Exists(legacyCache))
            {
                foreach (string path in Directory.EnumerateFiles(legacyCache, "*.json"))
                    MoveNewest(path, Path.Combine(target.Cache, Path.GetFileName(path)), failures);

                // Temp files of a save interrupted by a crash: never worth moving.
                foreach (string path in Directory.EnumerateFiles(legacyCache, "*.tmp"))
                    TryDelete(path, failures);
            }
            TryDelete(Path.Combine(legacyRoot, "settings.json.tmp"), failures);

            // Removed only when empty: a file we do not know keeps its folder.
            LocalFiles.DeleteFolderIfEmpty(legacyCache);
            LocalFiles.DeleteFolderIfEmpty(legacyRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Listing or removing a folder failed (opened in the file explorer...).
            failures.Add((Path.GetFileName(legacyRoot), ex));
        }
        return failures;
    }

    private static void MoveNewest(string from, string to, List<(string, Exception)> failures)
    {
        try
        {
            if (!File.Exists(from))
                return;

            // Already there (e.g. the old version was started again after
            // an update): keep the most recent of the two.
            if (File.Exists(to) && File.GetLastWriteTimeUtc(to) >= File.GetLastWriteTimeUtc(from))
            {
                File.Delete(from);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            // Keeps the write time, which the caches use as their fetch date.
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add((Path.GetFileName(from), ex));
        }
    }

    private static void TryDelete(string path, List<(string, Exception)> failures)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add((Path.GetFileName(path), ex));
        }
    }
}
