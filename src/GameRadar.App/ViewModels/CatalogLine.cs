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
public sealed class CatalogLine : FetchLine
{
    /// <summary>"Catalogue Steam": its block in Settings › Data.</summary>
    public required string Title { get; init; }

    /// <summary>The cached catalog (synchronous file read: run on a background thread).</summary>
    public required Func<GameCatalog?> LoadCached { get; init; }

    /// <summary>Downloads the catalog and replaces its cache.</summary>
    public required Func<Task<GameCatalog>> Refresh { get; init; }

    /// <summary>
    /// Its status line once loaded. Null: written by someone else (the Game Pass
    /// line counts the games played, known only once the history is built).
    /// </summary>
    public Func<GameCatalog, string>? Describe { get; init; }

    /// <summary>Its status line while it has no catalog (only with <see cref="Describe"/>).</summary>
    public string? NotLoadedStatus { get; init; }

    /// <summary>Downloaded at startup and by the button (PS Plus: only for a player who has a plan).</summary>
    public Func<bool> IsWanted { get; init; } = () => true;

    /// <summary>A new catalog changes the history's "Game Pass" badges: rebuilt after it (the Game Pass alone).</summary>
    public bool RebuildsHistory { get; init; }

    /// <summary>Its download takes minutes or tens of seconds: at startup, the cached catalogs recommend meanwhile.</summary>
    public bool SlowDownload { get; init; }

    /// <summary>Null until one is loaded or downloaded.</summary>
    public GameCatalog? Catalog { get; set; }

    /// <summary>None yet, or older than a week (every catalog: <see cref="CompactCatalogCache"/>).</summary>
    public bool NeedsDownload(DateTimeOffset now) => Catalog is null || CompactCatalogCache.IsStale(Catalog, now);

    /// <summary>Its status line from its catalog (nothing for a line written by someone else).</summary>
    public void ShowLoaded()
    {
        if (Describe is not null)
            ShowStatus(Catalog is null ? NotLoadedStatus ?? string.Empty : Describe(Catalog));
    }

    /// <summary>
    /// Downloads the catalog, then shows it on its line. On failure, the catalog
    /// it had is kept (see <see cref="FetchLine"/> for the error).
    /// </summary>
    public async Task<FetchResult> FetchAsync(NoticeCenter notices, bool banner = true)
    {
        var result = await RunFetchAsync(async () => Catalog = await Refresh(), notices, banner);
        if (result == FetchResult.Done)
            ShowLoaded();
        return result;
    }
}
