using System.Globalization;

namespace GameRadar.Core.Updates;

/// <summary>A part of a version's notes: "Évolutions", "Corrections"; no title for the notes written before the parts.</summary>
public sealed record ChangelogSection(string? Title, IReadOnlyList<string> Items);

/// <summary>One version of CHANGELOG.md: its number, its release date (null until published) and its news, by part.</summary>
public sealed record ChangelogEntry(Version Version, DateOnly? Date, IReadOnlyList<ChangelogSection> Sections)
{
    /// <summary>Every news item of the version, all parts together.</summary>
    public IReadOnlyList<string> Items => Sections.SelectMany(s => s.Items).ToList();
}

/// <summary>
/// Reads CHANGELOG.md, the single source of the release notes (the app shows
/// it, the GitHub releases are extracted from it). A tiny subset of
/// Markdown, enough for our notes and free of any dependency:
///   ## 1.2.0 — 2026-10-12     (version, then the date once released)
///   ### Évolutions            (a part of the version: new features, fixes)
///   - One news item           (indented lines continue the item above)
/// Anything else (title, introduction, blank lines) is ignored. A version
/// without parts (the older ones) has a single part without a title.
/// </summary>
public static class Changelog
{
    /// <summary>Entries from the newest version to the oldest. Never throws: an unreadable heading is skipped.</summary>
    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        // The parts of the current version (null outside a version), and the items of its last part.
        List<ChangelogSection>? sections = null;
        List<string>? items = null;

        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r', ' ', '\t');

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                // A part of the current version (ignored outside a version).
                if (sections is not null)
                {
                    items = [];
                    sections.Add(new ChangelogSection(line[4..].Trim(), items));
                }
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                // A heading that is not a version (e.g. "## Notes") ends the
                // current entry: its lines do not belong to the version above.
                sections = null;
                items = null;
                if (ParseHeading(line[3..]) is { } heading)
                {
                    sections = [];
                    entries.Add(new ChangelogEntry(heading.Version, heading.Date, sections));
                }
            }
            else if (sections is not null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                // Items before any part: an untitled part (the older versions).
                if (items is null)
                {
                    items = [];
                    sections.Add(new ChangelogSection(null, items));
                }
                items.Add(line[2..].Trim());
            }
            else if (items is { Count: > 0 } && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                // Continuation of a long item wrapped on several lines.
                items[^1] += " " + line.Trim();
            }
        }

        // A part without any item says nothing.
        return entries
            .Select(e => e with { Sections = e.Sections.Where(s => s.Items.Count > 0).ToList() })
            .OrderByDescending(e => e.Version)
            .ToList();
    }

    /// <summary>
    /// The entries worth a "what's new" after an update: newer than the
    /// version the user last saw, and not newer than the running one (a
    /// development build must not announce notes of an unreleased version).
    /// </summary>
    public static IReadOnlyList<ChangelogEntry> Unseen(IReadOnlyList<ChangelogEntry> entries, Version lastSeen, Version current) =>
        entries.Where(e => UpdateChecker.IsNewer(e.Version, lastSeen) && !UpdateChecker.IsNewer(e.Version, current)).ToList();

    // "1.2.0" or "1.2.0 — 2026-10-12" (em dash, en dash or hyphen).
    private static (Version Version, DateOnly? Date)? ParseHeading(string heading)
    {
        string[] parts = heading.Split(['—', '–', '-'], 2, StringSplitOptions.TrimEntries);
        if (!Version.TryParse(parts[0].TrimStart('v', 'V'), out var version))
            return null;

        DateOnly? date = parts.Length == 2
            && DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
        return (version, date);
    }
}
