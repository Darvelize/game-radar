using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GameRadar.Core.Settings;

namespace GameRadar.Core.Cache;

/// <summary>A cached payload and the moment it was saved.</summary>
public sealed record CacheEntry(string Content, DateTimeOffset SavedAt);

/// <summary>
/// Stores raw text payloads (typically API responses) on disk, one file per key,
/// in the cache folder of the <see cref="DataFolder"/> (%LOCALAPPDATA%\GameRadar\cache).
/// Generic on purpose: it knows nothing about Xbox or Game Pass, so any
/// connector can use it to spare API quota.
/// </summary>
public sealed class CacheStore
{
    private readonly string _folder;
    // Tripped by EraseAll (shared with the other stores when the app gives its own).
    private readonly EraseGuard _guard;
    // One merge at a time: two of them reading then writing the same
    // dictionary would each drop what the other added.
    private readonly SemaphoreSlim _mergeLock = new(1, 1);

    /// <param name="directory">Custom folder, mainly for tests. Null = default location.</param>
    /// <param name="guard">Shared "erased" state of the app's stores. Null = this store's own.</param>
    public CacheStore(string? directory = null, EraseGuard? guard = null)
    {
        _folder = directory ?? DataFolder.Default.Cache;
        _guard = guard ?? new EraseGuard();
    }

    /// <summary>
    /// Returns the cached entry, or null if there is none. Never throws:
    /// an unreadable cache is treated as missing, the app can always refetch.
    /// </summary>
    public CacheEntry? TryLoad(string key)
    {
        string path = GetPath(key);
        try
        {
            if (!File.Exists(path))
                return null;

            // The file's write time is the fetch time: no extra metadata file needed.
            var savedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            return new CacheEntry(File.ReadAllText(path), savedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The compact cache saved under this key as JSON (a catalog, a
    /// dictionary), or null when it is missing or unreadable: a corrupted
    /// cache counts as missing. Never throws. The JSON is read through a
    /// generated context (<paramref name="typeInfo"/>): no reflection.
    /// </summary>
    public T? LoadJson<T>(string key, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (TryLoad(key) is not { } entry)
            return null;

        try
        {
            return JsonSerializer.Deserialize(entry.Content, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The compact cache saved under this key as a JSON dictionary, or an
    /// empty one when it is missing or unreadable (it is simply rebuilt).
    /// </summary>
    public Dictionary<string, T> LoadDictionary<T>(string key, JsonTypeInfo<Dictionary<string, T>> typeInfo) =>
        LoadJson(key, typeInfo) ?? new();

    /// <summary>
    /// Deletes one cached payload, if there is one (a cache of a former
    /// format, once used). Never throws: a locked file is left for later.
    /// </summary>
    public void Delete(string key) => LocalFiles.TryDelete(GetPath(key));

    /// <summary>
    /// Deletes every cached payload (and a temp file left by a crash), then
    /// the folder if empty. Nothing is saved afterwards: the app quits.
    /// </summary>
    public void EraseAll()
    {
        _guard.Trip();
        LocalFiles.DeleteMatching(_folder, "*.json", "*.tmp");
        LocalFiles.DeleteFolderIfEmpty(_folder);
    }

    /// <summary>
    /// Adds entries to a compact cache (<see cref="LoadDictionary{T}(string, JsonTypeInfo{Dictionary{string, T}})"/>): the
    /// dictionary as saved NOW, plus these (same key: replaced). A lookup
    /// cancelled by a newer one saves what it found without undoing what the
    /// newer one saved meanwhile. Not cancellable, like any save of found data.
    /// </summary>
    public async Task MergeDictionaryAsync<T>(
        string key, IReadOnlyDictionary<string, T> entries, JsonTypeInfo<Dictionary<string, T>> typeInfo)
    {
        await _mergeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var saved = LoadDictionary(key, typeInfo);
            foreach (var (id, entry) in entries)
                saved[id] = entry;
            await SaveAsync(key, JsonSerializer.Serialize(saved, typeInfo)).ConfigureAwait(false);
        }
        finally
        {
            _mergeLock.Release();
        }
    }

    /// <summary>
    /// Saves a payload. Does nothing once the cache was erased: a lookup
    /// cancelled by "Effacer toutes mes données" still saves what it found
    /// (in a finally), which must not bring the files back.
    /// </summary>
    public Task SaveAsync(string key, string content, CancellationToken ct = default) =>
        // Two saves of one key at once (a cancelled lookup and its newer
        // replacement) each write their own temp file.
        LocalFiles.WriteAtomicallyAsync(GetPath(key),
            (file, token) => file.WriteAsync(Encoding.UTF8.GetBytes(content), token).AsTask(),
            _guard, ct);

    /// <summary>
    /// The keys of the cache files on disk (a temp file or a file that is not
    /// a valid key is skipped). Never throws: a missing folder has no keys.
    /// </summary>
    public IReadOnlyList<string> Keys()
    {
        try
        {
            return Directory.EnumerateFiles(_folder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(IsValidKey)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsValidKey(string key) =>
        !string.IsNullOrWhiteSpace(key) && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private string GetPath(string key)
    {
        // The key becomes a file name: reject anything that could escape
        // the cache folder (e.g. "../settings").
        if (!IsValidKey(key))
            throw new ArgumentException("Cache key must only contain letters, digits, '-' or '_'.", nameof(key));

        return Path.Combine(_folder, key + ".json");
    }
}
