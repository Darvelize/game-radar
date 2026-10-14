using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GameRadar.Core.Settings;

namespace GameRadar.Core.Cache;

/// <summary>
/// Downloads pictures once and keeps them on disk, in
/// the images folder of the <see cref="DataFolder"/> (%LOCALAPPDATA%\GameRadar\images).
/// Returns file paths only: decoding the picture is the UI's job.
/// </summary>
public sealed class ImageFileCache
{
    // Gentle with the image server, and enough to fill a screen quickly.
    public const int MaxParallelDownloads = 4;
    // A picture is small: one that takes longer is stalled (the server sent
    // its headers, then nothing). HttpClient.Timeout stops at the headers.
    private static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromSeconds(30);
    // Pictures, and downloads not finished (or cut short by a crash).
    private static readonly string[] OwnFiles = ["*.img", "*.tmp"];

    private readonly HttpClient _http;
    private readonly string _folder;
    private readonly TimeSpan _downloadTimeout;
    private readonly SemaphoreSlim _downloadSlots = new(MaxParallelDownloads, MaxParallelDownloads);
    // Tripped by EraseAll (shared with the other stores when the app gives its own).
    private readonly EraseGuard _guard;

    // Downloads in progress, by URL: two cards showing the same game share
    // one download instead of fetching the picture twice. Lazy guarantees the
    // download task is created only once, even if GetOrAdd runs its factory twice.
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inProgress = new();

    /// <param name="directory">Custom folder, mainly for tests. Null = default location.</param>
    /// <param name="downloadTimeout">Longest download of one picture; tests give a short one.</param>
    /// <param name="guard">Shared "erased" state of the app's stores. Null = this store's own.</param>
    public ImageFileCache(HttpClient http, string? directory = null, TimeSpan? downloadTimeout = null, EraseGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _folder = directory ?? DataFolder.Default.Images;
        _downloadTimeout = downloadTimeout ?? DefaultDownloadTimeout;
        _guard = guard ?? new EraseGuard();
    }

    /// <summary>A picture not shown for this long is deleted at the next start (<see cref="RemoveUnused"/>).</summary>
    public static readonly TimeSpan UnusedLifetime = TimeSpan.FromDays(30);

    /// <summary>The picture already on disk, or null: no download, no wait.</summary>
    public string? CachedPath(string url)
    {
        string path = PathFor(url);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Marks a picture as shown, for <see cref="RemoveUnused"/>: its write time
    /// (NTFS often does not update the access time). At most once a day: one
    /// disk write, not one per click. A disk call: off the UI thread. Never throws.
    /// </summary>
    public static void MarkShown(string path)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (now - File.GetLastWriteTimeUtc(path) > TimeSpan.FromDays(1))
                File.SetLastWriteTimeUtc(path, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not marked: at worst downloaded again after RemoveUnused.
        }
    }

    /// <summary>
    /// Deletes the pictures not shown for <see cref="UnusedLifetime"/> before
    /// the last one shown (games gone from the catalogs, changed addresses):
    /// a player back after two months keeps theirs. Called at start, before
    /// any download. Never throws.
    /// </summary>
    public void RemoveUnused()
    {
        try
        {
            var folder = new DirectoryInfo(_folder);
            if (!folder.Exists)
                return;
            var files = OwnFiles.SelectMany(folder.EnumerateFiles).ToList();
            if (files.Count == 0)
                return;
            var limit = files.Max(f => f.LastWriteTimeUtc) - UnusedLifetime;
            foreach (var file in files.Where(f => f.LastWriteTimeUtc < limit))
                LocalFiles.TryDelete(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Folder unreadable: nothing deleted, the app starts anyway.
        }
    }

    /// <summary>
    /// Local path of the picture, downloading it the first time.
    /// Returns null when it cannot be obtained: a missing picture must never
    /// break the screen, the UI simply keeps its placeholder.
    /// </summary>
    public Task<string?> GetAsync(string url)
    {
        if (CachedPath(url) is { } cached)
            return Task.FromResult<string?>(cached);
        string path = PathFor(url);

        return _inProgress.GetOrAdd(url, u => new Lazy<Task<string?>>(() => DownloadAsync(u, path))).Value;
    }

    private async Task<string?> DownloadAsync(string url, string path)
    {
        try
        {
            await _downloadSlots.WaitAsync().ConfigureAwait(false);
            try
            {
                // The whole download, body included: a stalled one frees its slot.
                using var timeout = new CancellationTokenSource(_downloadTimeout);
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode
                    || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.Ordinal) != true)
                    return null;

                // Through a temp file: a half-downloaded file is never mistaken
                // for a complete picture. Nothing written once erased.
                bool written = await LocalFiles.WriteAtomicallyAsync(path,
                    response.Content.CopyToAsync, _guard, timeout.Token).ConfigureAwait(false);
                return written ? path : null;
            }
            finally
            {
                _downloadSlots.Release();
            }
        }
        // Network, disk, timeout, an address HttpClient cannot use (relative,
        // unknown scheme): never an exception, the card keeps its placeholder.
        catch (Exception)
        {
            return null;
        }
        finally
        {
            // Done (or failed): a later request checks the disk, or retries.
            _inProgress.TryRemove(url, out _);
        }
    }

    /// <summary>
    /// Deletes every downloaded picture (and unfinished downloads), then the
    /// folder if empty. Nothing is written afterwards: the app quits.
    /// </summary>
    public void EraseAll()
    {
        _guard.Trip();
        LocalFiles.DeleteMatching(_folder, OwnFiles);
        LocalFiles.DeleteFolderIfEmpty(_folder);
    }

    // The URL itself cannot be a file name (slashes, '?', length): its
    // SHA-256 gives a short, unique and stable name instead.
    private string PathFor(string url)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(_folder, hash + ".img");
    }
}
