using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace GameRadar.Core.Services;

/// <summary>
/// A game's own title, whatever the store adds to it: editions, platforms,
/// previews. One rule for every comparison of titles between platforms
/// (played game excluded from the recommendations, editions deduplicated,
/// Game Pass and Steam entries merged, Xbox and Steam histories merged,
/// Steam search), so that "Control" and "Control Ultimate Edition - Xbox
/// Series X|S" are one game everywhere.
/// </summary>
// "partial": the regex source generator adds the body of the regex methods.
public static partial class GameTitle
{
    private static readonly char[] Dashes = ['-', '–', '—'];

    /// <summary>
    /// The title without trademark symbols, store additions ("(Aperçu)"),
    /// platform ("(PC)", "pour Xbox Series X|S", a final "PC"), edition after
    /// a spaced dash ("- Pack Cross-gen") or edition name ("Standard Edition",
    /// "édition Jeu de l'année"). Kept: a year ("Dead Space (2008)" is not the
    /// remake), "Remastered", numbers ("Hades" ≠ "Hades II"), inner hyphens.
    /// </summary>
    public static string Clean(string title)
    {
        string text = Symbols().Replace(title, string.Empty);
        text = StoreAdditions().Replace(text, string.Empty);
        text = FrenchEditionPrefix().Replace(text, string.Empty);
        // Only platform words: "Dead Space (2008)" must stay apart from the remake.
        text = PlatformSuffix().Replace(text, string.Empty);

        // Only a spaced dash separates an edition; "Half-Life" keeps its hyphen.
        // Any dash: "-", "–", or "—" ("The Witcher 3: Wild Hunt — Remastered").
        // Searched from the 2nd character: a title never starts with its edition.
        int dash = text.Length > 1 ? text.IndexOfAny(Dashes, 1) : -1;
        while (dash > 0 && !(text[dash - 1] == ' ' && dash + 1 < text.Length && text[dash + 1] == ' '))
            dash = text.IndexOfAny(Dashes, dash + 1);
        if (dash > 0)
            text = text[..dash];

        text = ColonSpacing().Replace(text, ": ");
        text = EditionSuffix().Replace(text, string.Empty);
        return text.Trim();
    }

    /// <summary>
    /// Comparison key: the cleaned title in lowercase letters and digits.
    /// "Sleeping Dogs™" and "Sleeping Dogs: Definitive Edition" give the same key.
    /// </summary>
    public static string Key(string title)
    {
        if (RememberedKeys.TryGetValue(title, out var key))
            return key;
        key = CatalogMatcher.NormalizeName(Clean(title));
        if (RememberedKeys.Count < MaxRememberedKeys)
            RememberedKeys.TryAdd(title, key);
        return key;
    }

    // Each recommendation computation keys the same ~2,300 titles 5 or 6
    // times (exclusion, deduplication, merges, tags), at ~6 µs each (the
    // regexes): remembered, thread-safe (computed off the UI thread). Bounded:
    // the titles typed in "Ajouter un jeu" add a few each.
    private const int MaxRememberedKeys = 20_000;
    private static readonly ConcurrentDictionary<string, string> RememberedKeys = new();

    [GeneratedRegex("[™®©]")]
    private static partial Regex Symbols();

    [GeneratedRegex(@"\s*\((Aperçu|Preview|Game Preview|Windows|PC)\)", RegexOptions.IgnoreCase)]
    private static partial Regex StoreAdditions();

    // A trailing platform, in the three forms seen in the Store:
    //   "Skyrim Special Edition (PC)"      -> in parentheses
    //   "Battlefield 2042 sur Xbox One"    -> after "sur" / "pour" / "for" ("for PlayStation 4" too)
    //   "Overwatch PC", "Dead by Daylight Windows", "Enlisted Xbox One"
    //                                      -> a bare platform at the end
    // PlayStation titles say their console versions the same ways, in square
    // brackets too: "Lake PS4 & PS5", "Dead Cells (PS4 & PS5)", "Hogwarts
    // Legacy PS4 Version", "Green Hell PS4 + PS5 Free Upgrade", and in full:
    // "Wreckfest PlayStation®5 Version" (the symbol is gone by then).
    // [GeneratedRegex]: the regex code is generated at build time (faster,
    // and checked by the compiler) instead of being parsed at run time.
    [GeneratedRegex(
        @"\s*([\(\[][^)\]]*\b(PC|Windows|Xbox|PS[2-5]|PlayStation)\b[^)\]]*[\)\]]|\b(sur|pour|for)\s+(PC|Windows|Xbox|PS[45]|PlayStation)\b.*|\b(PC|Windows|Xbox One|Xbox Series X\|S|Xbox 360)|\b(PS|PlayStation\s*)[45]\s*(&|\+|and)?\s*((PS|PlayStation\s*)[45])?(\s+(Version|Free Upgrade))?)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex PlatformSuffix();

    // French edition names may come first: "Édition Deluxe de Mass Effect: Andromeda".
    [GeneratedRegex(@"^\s*(É|E)dition\s+[^:]*?\s+(de|du|d')\s*", RegexOptions.IgnoreCase)]
    private static partial Regex FrenchEditionPrefix();

    [GeneratedRegex(@"\s*:\s*")]
    private static partial Regex ColonSpacing();

    // "... Standard Edition", "...: Game of the Year Edition", "... Édition EA Play",
    // a platform's edition: "FINAL FANTASY XV WINDOWS EDITION", "Anno 1800 Console Edition", "Goat
    // Simulator 3: Xbox One Edition" (the platform word stayed in the key: the
    // game was recommended to whoever finished it on another platform), "... Anniversary
    // Edition" (the edition still sold of "Titan Quest"; "Halo: Combat Evolved
    // Anniversary", without "Edition", is a remake: another game).
    [GeneratedRegex(
        @"[\s:]+((Standard|Deluxe|Ultimate|Gold|Complete|Definitive|Anniversary|Premium|Digital Deluxe|Enhanced|Game of the Year|GOTY|Windows|PC|Console|Xbox Series X\|S|Xbox Series|Xbox One|Xbox 360|Xbox)\s+)?(Edition|Édition)\b.*$|\s+(É|E)dition\s+.*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex EditionSuffix();
}
