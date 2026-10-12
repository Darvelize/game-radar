using GameRadar.Core.Services;

namespace GameRadar.Core.Steam;

/// <summary>
/// The Steam tags a player may give a game, in the app's language: the style
/// tags (those the computation uses, not "Free to Play"...), sorted by name.
/// </summary>
public sealed class SteamTagChoices
{
    private readonly Dictionary<string, int> _idByName = [];

    /// <param name="names">Every tag's name (tag id → name), from <see cref="SteamTagSource.GetTagNamesAsync"/>.</param>
    public SteamTagChoices(IReadOnlyDictionary<int, string> names)
    {
        Names = names;
        Offered = names
            .Where(t => SteamTagClient.IsStyle(t.Key))
            .OrderBy(t => t.Value, StringComparer.CurrentCulture)
            .Select(t => (t.Key, t.Value))
            .ToList();
        foreach (var (id, name) in Offered)
            _idByName.TryAdd(CatalogMatcher.NormalizeName(name), id);
    }

    /// <summary>Every tag's name (tag id → name), the non-style ones too.</summary>
    public IReadOnlyDictionary<int, string> Names { get; }

    /// <summary>The tags offered, by name.</summary>
    public IReadOnlyList<(int Id, string Name)> Offered { get; }

    /// <summary>The offered tag a typed or picked name stands for (case, accents and symbols ignored).</summary>
    public int? IdOf(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null
            : _idByName.TryGetValue(CatalogMatcher.NormalizeName(text), out int id) ? id : null;
}
