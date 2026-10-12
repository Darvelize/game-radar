using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;

namespace GameRadar.App.ViewModels;

/// <summary>
/// What the history, the add-a-game search and the recommendations all read:
/// the candidate catalogs, and the games the player has played.
/// The catalogs are those of the CatalogsViewModel lines (loaded at startup,
/// then refreshed), named here for their readers.
/// </summary>
public sealed class LibraryState(AccountsViewModel accounts, SettingsSaver saver, CatalogsViewModel catalogs)
{
    /// <summary>The Game Pass catalog; null until one is loaded or downloaded.</summary>
    public GameCatalog? Catalog => catalogs.GamePass.Catalog;

    /// <summary>The Steam store candidates; null until loaded or downloaded.</summary>
    public GameCatalog? SteamCatalog => catalogs.Steam.Catalog;

    /// <summary>The PS Plus catalog; null until loaded or downloaded (never downloaded without a plan).</summary>
    public GameCatalog? PsPlusCatalog => catalogs.PsPlus.Catalog;

    /// <summary>The GOG store candidates; null until loaded or downloaded.</summary>
    public GameCatalog? GogCatalog => catalogs.Gog.Catalog;

    /// <summary>The Epic store candidates; null until loaded or downloaded.</summary>
    public GameCatalog? EpicCatalog => catalogs.Epic.Catalog;

    /// <summary>The PlayStation Store candidates; null until loaded or downloaded (never downloaded with the box unticked).</summary>
    public GameCatalog? PsStoreCatalog => catalogs.PsStore.Catalog;

    /// <summary>
    /// The Steam tags known and their names in the app's language, set by the
    /// recommendations computation (from the cache, then with the tags it
    /// found); null before its first run. The history shows them too.
    /// </summary>
    public (SteamTagIndex Index, IReadOnlyDictionary<int, string> Names)? Tags { get; private set; }

    /// <summary>New tags are known: whoever shows them updates its labels.</summary>
    public event Action? TagsChanged;

    public void SetTags(SteamTagIndex index, IReadOnlyDictionary<int, string> names)
    {
        Tags = (index, names);
        TagsChanged?.Invoke();
    }

    /// <summary>Some candidate catalog loaded: one is enough to recommend (another download may have failed).</summary>
    public bool HasCatalog => catalogs.Lines.Any(l => l.Catalog is not null);

    /// <summary>The games added by hand, as played games (platform "Manual" or the one chosen).</summary>
    private IEnumerable<PlayedGame> ManualHistory() => saver.Settings.ManualGames.Select(ManualGames.ToPlayed);

    /// <summary>A game played on PlayStation, synced or added by hand: the "PlayStation Store" box is ticked by default.</summary>
    public bool HasPlayStationHistory =>
        accounts.PlayStationSync.Games.Count > 0
        || saver.Settings.ManualGames.Any(g => g.Platform == ManualPlatform.PlayStation);

    /// <summary>Something to show: a synced history, or games added by hand (no account needed).</summary>
    public bool HasAnyHistory =>
        accounts.SyncedPlatforms.Any(p => p.History is not null) || saver.Settings.ManualGames.Count > 0;

    /// <summary>The games of every synced platform, most recent first, then the games added by hand.</summary>
    public IReadOnlyList<PlayedGame> CombinedHistory() =>
        [.. HistoryMerger.Combine([.. accounts.SyncedPlatforms.Select(p => p.Games)]), .. ManualHistory()];
}
