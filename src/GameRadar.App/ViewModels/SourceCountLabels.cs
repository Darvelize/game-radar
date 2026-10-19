using GameRadar.Core.Models;

namespace GameRadar.App.ViewModels;

/// <summary>
/// The games of each source, written for the sources menu of "Pour toi" ("2 000"):
/// a source without games (catalog not loaded, PlayStation Store unticked) shows none.
/// </summary>
public sealed class SourceCountLabels(IReadOnlyDictionary<CatalogSource, int> counts)
{
    public string GamePass { get; } = Label(counts, CatalogSource.GamePass);
    public string PsPlus { get; } = Label(counts, CatalogSource.PsPlus);
    public string Steam { get; } = Label(counts, CatalogSource.Steam);
    public string Gog { get; } = Label(counts, CatalogSource.Gog);
    public string Epic { get; } = Label(counts, CatalogSource.Epic);
    public string PlayStationStore { get; } = Label(counts, CatalogSource.PlayStationStore);
    public string MyGames { get; } = Label(counts, CatalogSource.MyGames);

    private static string Label(IReadOnlyDictionary<CatalogSource, int> counts, CatalogSource source) =>
        counts.GetValueOrDefault(source) is > 0 and var count ? Localization.Format(Strings.Reco_SourceCount, count) : string.Empty;
}
