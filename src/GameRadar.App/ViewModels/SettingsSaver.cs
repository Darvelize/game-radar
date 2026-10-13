using GameRadar.Core.Settings;

namespace GameRadar.App.ViewModels;

/// <summary>
/// The live settings, and their writes to settings.json, one at a time: every
/// save goes through the same temp file, so quick clicks would otherwise collide.
/// Each save is kept by <paramref name="work"/> until written (the tests wait for it).
/// </summary>
public sealed class SettingsSaver(SettingsStore store, AppSettings settings, BackgroundWork? work = null)
{
    // Async-friendly lock (a plain lock cannot be held across an await).
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    // Saves started and not written yet. Most are fire-and-forget ("_ = "):
    // nobody awaits them, so this is how one knows a save is still writing.
    private int _pendingSaves;

    /// <summary>The settings the view models read and change; saved as they are when a save runs.</summary>
    public AppSettings Settings { get; } = settings;

    /// <summary>A save is still writing: settings.json (or its temp file) may change.</summary>
    public bool IsSaving => _pendingSaves > 0;

    private readonly BackgroundWork _work = work ?? new();

    public Task SaveAsync() => _work.Track(WriteAsync());

    private async Task WriteAsync()
    {
        // Counted before waiting for the lock: a save queued behind another is pending too.
        _pendingSaves++;
        await _saveLock.WaitAsync();
        try
        {
            // Snapshot taken inside the lock, so the last save always writes
            // the latest choices. Still on the UI thread (no ConfigureAwait
            // in view models), where the live settings are modified; the
            // serialization itself may then run on another thread.
            await store.SaveAsync(Settings.Clone());
        }
        finally
        {
            // In a finally block: an error must never leave the lock taken.
            _saveLock.Release();
            _pendingSaves--;
        }
    }

    /// <summary>
    /// Completes once every save started so far is written: the saves queue
    /// on the lock in order, so taking it after them means they are done. For
    /// whoever must not run during a save (the tests cleaning up their folder).
    /// </summary>
    public async Task WhenSavedAsync()
    {
        await _saveLock.WaitAsync();
        _saveLock.Release();
    }
}
