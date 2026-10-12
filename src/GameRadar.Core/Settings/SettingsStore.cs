using System.Text.Json;

namespace GameRadar.Core.Settings;

/// <summary>
/// Saves settings as JSON in the root of the <see cref="DataFolder"/>
/// (%LOCALAPPDATA%\GameRadar on Windows).
/// Each person who uses the app gets their own file, with their own key.
/// </summary>
public sealed class SettingsStore
{

    private readonly string _filePath;

    // Tripped by EraseAll (shared with the other stores when the app gives its own).
    private readonly EraseGuard _guard;

    // Set when an existing file could not be read: the next save puts it
    // aside (settings.json.bad) instead of overwriting the player's choices.
    private bool _putAsideBeforeSave;

    /// <param name="directory">Custom folder, mainly for tests. Null = default location.</param>
    /// <param name="guard">Shared "erased" state of the app's stores. Null = this store's own.</param>
    public SettingsStore(string? directory = null, EraseGuard? guard = null)
    {
        string folder = directory ?? DataFolder.Default.Root;

        _filePath = Path.Combine(folder, "settings.json");
        _guard = guard ?? new EraseGuard();
    }

    /// <summary>
    /// Loads settings. Never throws: a missing or corrupted file simply
    /// gives default settings, so the app can always start.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            string json = File.ReadAllText(_filePath);
            return WithoutNullLists(JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupted (a power cut), or locked for a moment: the player's
            // choices may still be in it, kept aside at the next save.
            _putAsideBeforeSave = true;
            return new AppSettings();
        }
    }

    // A JSON null replaces a list's "= []" initializer: read as empty, the
    // code relying on non-null lists (Clone, the taste, the migrations) holds.
    private static AppSettings WithoutNullLists(AppSettings settings)
    {
        settings.ExcludedFromTaste ??= [];
        settings.IncludedInTaste ??= [];
        settings.MarkedCompleted ??= [];
        settings.NotInterested ??= [];
        settings.ManualGames ??= [];
        settings.AddedTags ??= [];
        // A null list inside (a file edited by hand) would break Clone and the tags index.
        foreach (var key in settings.AddedTags.Where(e => e.Value is null).Select(e => e.Key).ToList())
            settings.AddedTags.Remove(key);
        return settings;
    }

    /// <summary>True once settings were saved at least once: tells a returning user from a first launch.</summary>
    public bool Exists => File.Exists(_filePath);

    /// <summary>
    /// Deletes the settings file, then its folder when nothing else is left
    /// in it (the cache folder must be erased first, see <see cref="LocalDataEraser"/>).
    /// </summary>
    public void EraseAll()
    {
        _guard.Trip();
        // The temp files of a save (one per write, a unique name) and the one of an older version.
        LocalFiles.DeleteMatching(Path.GetDirectoryName(_filePath)!, Path.GetFileName(_filePath) + ".*tmp");
        LocalFiles.Delete(_filePath, _filePath + ".bad");
        LocalFiles.DeleteFolderIfEmpty(Path.GetDirectoryName(_filePath)!);
    }

    /// <summary>
    /// Saves the settings; does nothing once the app erased its data
    /// (a choice clicked just before the erasure is saved in the background).
    /// </summary>
    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (_guard.IsErased)
            return;

        if (_putAsideBeforeSave && File.Exists(_filePath))
            File.Copy(_filePath, _filePath + ".bad", overwrite: true);
        _putAsideBeforeSave = false;

        // Through a temp file swapped in: if the app crashes mid-write, the
        // previous settings file is not left half-written.
        await LocalFiles.WriteAtomicallyAsync(
            _filePath, (file, token) => JsonSerializer.SerializeAsync(file, settings, SettingsJsonContext.Default.AppSettings, token), _guard, ct).ConfigureAwait(false);
    }
}
