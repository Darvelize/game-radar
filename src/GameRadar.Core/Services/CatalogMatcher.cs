using System.Text;
using GameRadar.Core.Models;
using GameRadar.Core.Xbox;

namespace GameRadar.Core.Services;

/// <summary>
/// Finds which played games are in the Game Pass catalog.
/// Tries the most reliable key first: Xbox title id, then Store package
/// name, then normalized name (for games whose ids differ, e.g. the Halo
/// Master Chief Collection), then the game's own title (<see cref="GameTitle.Key"/>:
/// the history says "Avowed", the Store sells "Avowed Standard Edition").
/// </summary>
public sealed class CatalogMatcher
{
    // Dictionaries built once: each lookup is then O(1) instead of
    // scanning the ~800 catalog games for every played game.
    private readonly Dictionary<string, CatalogGame> _byTitleId = new();
    private readonly Dictionary<string, CatalogGame> _byPackage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CatalogGame> _byName = new();
    private readonly Dictionary<string, CatalogGame> _byTitleKey = new();

    public CatalogMatcher(IEnumerable<CatalogGame> catalog)
    {
        foreach (var game in catalog)
        {
            // TryAdd: when two products share a key (editions of the same
            // game), the first one wins instead of throwing.
            if (game.XboxTitleId is { } titleId)
                _byTitleId.TryAdd(titleId, game);
            if (game.PackageFamilyName is { } package)
                _byPackage.TryAdd(package, game);
            if (NormalizeName(game.Title) is { Length: > 0 } name)
                _byName.TryAdd(name, game);
            if (GameTitle.Key(game.Title) is { Length: > 0 } key)
                _byTitleKey.TryAdd(key, game);
        }
    }

    /// <summary>Returns the matching Game Pass game, or null if the game is not in the catalog.</summary>
    public CatalogGame? Find(PlayedGame played)
    {
        if (played.Platform == XboxLiveClient.PlatformName
            && _byTitleId.TryGetValue(played.PlatformId, out var byId))
            return byId;

        if (played.PackageFamilyName is { } package
            && _byPackage.TryGetValue(package, out var byPackage))
            return byPackage;

        // The exact name first: of two editions, the one named like the game.
        return _byName.GetValueOrDefault(NormalizeName(played.Name))
            ?? _byTitleKey.GetValueOrDefault(GameTitle.Key(played.Name));
    }

    /// <summary>
    /// Reduces a title to lowercase letters and digits, without accents:
    /// "WATCH_DOGS™" and "Watch Dogs" both become "watchdogs".
    /// </summary>
    public static string NormalizeName(string name)
    {
        // FormKD splits "é" into "e" + a combining accent, which is then
        // dropped along with every symbol (®, punctuation, spaces), and turns
        // compatibility characters into plain ones: "Ⅻ" (one letter) is XII.
        // It would also turn ™ and ℠ into the letters "TM", "SM": removed first.
        var builder = new StringBuilder(name.Length);
        foreach (char c in name.Replace("™", "").Replace("℠", "").Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }
}
