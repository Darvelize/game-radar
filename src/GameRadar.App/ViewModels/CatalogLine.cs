using CommunityToolkit.Mvvm.ComponentModel;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;

namespace GameRadar.App.ViewModels;

/// <summary>
/// One candidate catalog (Game Pass, PS Plus, Steam, GOG) as the app sees it:
/// its catalog, how it is read, downloaded and judged stale, and its status line
/// in Settings › Data. What differs from one catalog to the next is given when it
/// is created (CatalogsViewModel), so a new catalog is one more entry in a list.
/// What a catalog means for the recommendations (plans, merge, filter boxes)
/// stays written per catalog.
/// </summary>
public sealed partial class CatalogLine : ObservableObject
{
    /// <summary>"Catalogue Steam": its block in Settings › Data.</summary>
    public required string Title { get; init; }

    /// <summary>Context of its errors in the log, in fixed English.</summary>
    public required string LogContext { get; init; }

    public required string FetchingStatus { get; init; }

    /// <summary>The cached catalog (synchronous file read: run on a background thread).</summary>
    public required Func<GameCatalog?> LoadCached { get; init; }

    /// <summary>Downloads the catalog and replaces its cache.</summary>
    public required Func<Task<GameCatalog>> Refresh { get; init; }

    /// <summary>The message of a failed download, from the most specific error to the most generic.</summary>
    public required Func<Exception, string> ErrorMessage { get; init; }

    /// <summary>
    /// Its status line once loaded. Null: written by someone else (the Game Pass
    /// line counts the games played, known only once the history is built).
    /// </summary>
    public Func<GameCatalog, string>? Describe { get; init; }

    /// <summary>Its status line while it has no catalog (only with <see cref="Describe"/>).</summary>
    public string? NotLoadedStatus { get; init; }

    /// <summary>Downloaded at startup and by the button (PS Plus: only for a player who has a plan).</summary>
    public Func<bool> IsWanted { get; init; } = () => true;

    /// <summary>A new catalog changes the history badges ("Game Pass", store links): rebuilt after it.</summary>
    public bool RebuildsHistory { get; init; } = true;

    /// <summary>Its download takes minutes or tens of seconds: at startup, the cached catalogs recommend meanwhile.</summary>
    public bool SlowDownload { get; init; }

    /// <summary>Null until one is loaded or downloaded.</summary>
    public GameCatalog? Catalog { get; set; }

    /// <summary>Its catalog state, or the current operation.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Its last download failed: the error stays on its line until the next one.</summary>
    public bool Failed { get; private set; }

    // Being downloaded: asked again meanwhile (the button during the startup
    // refresh), it is not downloaded twice at once; its line says so until it ends.
    private bool _fetching;

    /// <summary>None yet, or older than a week (every catalog: <see cref="CompactCatalogCache"/>).</summary>
    public bool NeedsDownload(DateTimeOffset now) => Catalog is null || CompactCatalogCache.IsStale(Catalog, now);

    /// <summary>Its status line from its catalog (nothing for a line written by someone else).</summary>
    public void ShowLoaded()
    {
        if (Describe is not null)
            ShowStatus(Catalog is null ? NotLoadedStatus ?? string.Empty : Describe(Catalog));
    }

    /// <summary>
    /// Writes its status line, unless it is being downloaded: it says so until the end,
    /// and a history rebuild or a search must not write the old catalog over it.
    /// </summary>
    public void ShowStatus(string status)
    {
        if (!_fetching)
            Status = status;
    }

    /// <summary>
    /// Downloads the catalog. Returns false on failure (the error is on its status
    /// line, in the banner and in the log, and the catalog it had is kept), or when
    /// it is already being downloaded.
    /// </summary>
    public async Task<bool> FetchAsync(NoticeCenter notices)
    {
        if (_fetching)
            return false;
        _fetching = true;
        Status = FetchingStatus;
        try
        {
            // No ConfigureAwait(false): back on the UI thread to update the line.
            Catalog = await Refresh();
        }
        catch (Exception ex)
        {
            Failed = true;
            Status = ErrorMessage(ex);
            notices.Report(LogContext, Status, ex);
            return false;
        }
        finally
        {
            _fetching = false;
        }
        Failed = false;
        ShowLoaded();
        return true;
    }
}
