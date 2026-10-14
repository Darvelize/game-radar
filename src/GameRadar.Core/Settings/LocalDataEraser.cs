using System.Runtime.ExceptionServices;
using GameRadar.Core.Cache;
using GameRadar.Core.Logging;

namespace GameRadar.Core.Settings;

/// <summary>
/// "Effacer toutes mes données": everything GameRadar keeps on this computer
/// (accounts, settings, caches, pictures, error logs). Each store deletes only the files
/// it knows, never a whole folder blindly: a user may have put something
/// else there (even GameRadar.exe itself).
/// </summary>
public sealed class LocalDataEraser(
    SettingsStore settings, CacheStore cache, SecretStore secrets, ImageFileCache images, ErrorLog log, DataFolder data)
{
    /// <summary>
    /// The secrets first (Xbox session, Steam key, NPSSO: the most sensitive),
    /// then every other store, each one tried even when another failed (a
    /// file opened by another program): as much as possible is erased. The
    /// settings come last: the GameRadar folder is empty, and removed, when
    /// they are deleted, except in portable mode: the "Data" folder is the
    /// user's own choice and stays, empty (without it the next launch would
    /// go back to the normal mode without saying so). Throws the first
    /// failure, once everything was tried.
    /// </summary>
    public void EraseAll()
    {
        // Before the first deletion: from now on no store of the app writes
        // a file back, whichever thread it runs on.
        data.Guard.Trip();
        Exception? firstFailure = null;
        foreach (Action erase in new Action[] { secrets.EraseAll, cache.EraseAll, images.EraseAll, log.EraseAll, settings.EraseAll, KeepPortableFolder })
        {
            try
            {
                erase();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                firstFailure ??= ex;
            }
        }
        if (firstFailure is not null)
        {
            // The app stays open (it reports the failure, the player retries):
            // it must keep saving what they choose meanwhile.
            data.Guard.Reset();
            ExceptionDispatchInfo.Throw(firstFailure);
        }
    }

    private void KeepPortableFolder()
    {
        if (data.IsPortable)
            Directory.CreateDirectory(data.Root);
    }
}

/// <summary>File writes and deletions shared by the stores (their saves and their EraseAll).</summary>
internal static class LocalFiles
{
    /// <summary>
    /// Writes a file through a temp file swapped in: a crash mid-write never
    /// leaves a half-written file. One temp file per write (a unique name):
    /// two writes of one file at once each have theirs, removed on failure.
    /// Nothing is written once <paramref name="guard"/> is tripped ("Effacer
    /// toutes mes données"): a cancelled lookup still saves what it found, a
    /// picture still arrives; what is written during the erasure goes at once.
    /// Returns false when nothing stays written.
    /// </summary>
    public static async Task<bool> WriteAtomicallyAsync(
        string path, Func<Stream, CancellationToken, Task> write, EraseGuard guard, CancellationToken ct)
    {
        if (guard.IsErased)
            return false;

        string folder = Path.GetDirectoryName(path)!;
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(folder);
            // ConfigureAwait(false) on the disposal too: SecretStore waits for this method synchronously.
            var file = File.Create(tempPath);
            await using (file.ConfigureAwait(false))
            {
                await write(file, ct).ConfigureAwait(false);
            }
            await MoveOverAsync(tempPath, path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            // The erasure deleted its temp file or folder under its feet.
            if (ex is IOException or UnauthorizedAccessException && guard.IsErased)
                return false;
            throw;
        }

        if (!guard.IsErased)
            return true;
        // Erased while it was writing: what it wrote goes too.
        TryDelete(path);
        try
        {
            DeleteFolderIfEmpty(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    // Windows refuses for an instant to replace a file another write is
    // replacing, or one being read (2 times in 15, 8 saves of one key at once):
    // a few short retries, then the error.
    private static async Task MoveOverAsync(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5)
            {
                await Task.Delay(20 * attempt).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Deletes a file if it can: a locked one is left for later. Never throws.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Delete(params string[] paths)
    {
        // File.Delete accepts a missing file but throws when its FOLDER is
        // missing (DirectoryNotFoundException): checking the file covers both.
        foreach (string path in paths.Where(File.Exists))
            File.Delete(path);
    }

    public static void DeleteMatching(string folder, params string[] patterns)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (string pattern in patterns)
            foreach (string path in Directory.EnumerateFiles(folder, pattern))
                File.Delete(path);
    }

    public static void DeleteFolderIfEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);
    }
}
