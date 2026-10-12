namespace GameRadar.App;

/// <summary>
/// The legal texts shipped inside the exe (the legal\ folder of the
/// repository, as embedded resources): GameRadar's terms of use, and the
/// licenses of the open-source components it contains, which ask to come
/// with every copy. Also published as they are next to each release; the
/// app cuts them up to show them readably.
/// </summary>
public static class LegalDocuments
{
    public const string Terms = "LICENSE.txt";
    public const string ThirdPartyNotices = "THIRD-PARTY-NOTICES.txt";

    // Top-level rule of THIRD-PARTY-NOTICES.txt (tools/Update-ThirdPartyNotices.ps1):
    // every section is this rule, its title, the rule again, then its text.
    private static readonly string SectionRule = new('=', 78);

    /// <summary>The whole text, line endings as "\n".</summary>
    public static string Read(string name)
    {
        using var stream = typeof(LegalDocuments).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is not embedded in the app.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>
    /// The terms of use, one part per language (the file is bilingual: its
    /// "FRANÇAIS" and "ENGLISH" headings), plus the title, copyright and
    /// version lines above them, common to both.
    /// </summary>
    public static TermsOfUse ReadTerms()
    {
        var paragraphs = Paragraphs(Read(Terms));
        int french = paragraphs.FindIndex(p => p.IsHeading && p.Text == "FRANÇAIS");
        int english = paragraphs.FindIndex(p => p.IsHeading && p.Text == "ENGLISH");
        if (french < 1 || english < french)
            throw new InvalidOperationException($"{Terms} lost its FRANÇAIS / ENGLISH headings.");

        return new TermsOfUse(
            paragraphs[0].Text,
            paragraphs.GetRange(french + 1, english - french - 1),
            paragraphs.GetRange(english + 1, paragraphs.Count - english - 1));
    }

    /// <summary>
    /// The components list, read line by line, and the other sections of the
    /// notices (license texts, notices of what a component bundles), to be
    /// shown folded: ~220 KB in all.
    /// </summary>
    public static ThirdPartyNoticesText ReadThirdPartyNotices()
    {
        // [0] file title, [1] introduction, then (section title, section text) pairs.
        string[] parts = Read(ThirdPartyNotices).Split(SectionRule + "\n");
        var components = new List<LicensedComponent>();
        var sections = new List<NoticeSection>();

        for (int i = 2; i + 1 < parts.Length; i += 2)
        {
            string title = parts[i].Trim();
            string body = parts[i + 1];
            if (title == "Components")
                components.AddRange(ParseComponents(body));
            else
                sections.Add(new NoticeSection(title, Paragraphs(body)));
        }
        return new ThirdPartyNoticesText(components, sections);
    }

    // "Avalonia 12.1.3\n  Copyright…\n  License: MIT\n  https://…", blocks
    // separated by blank lines.
    private static IEnumerable<LicensedComponent> ParseComponents(string body)
    {
        foreach (string block in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string[] lines = block.Trim('\n').Split('\n');
            string[] nameAndVersion = lines[0].Trim().Split(' ');
            if (nameAndVersion.Length != 2)
                continue;
            var details = lines.Skip(1).Select(l => l.Trim()).ToList();
            string license = details.FirstOrDefault(l => l.StartsWith("License: ", StringComparison.Ordinal))?["License: ".Length..] ?? "";
            string? url = details.FirstOrDefault(l => l.StartsWith("https://", StringComparison.Ordinal));
            string? copyright = details.FirstOrDefault(l => l != url && !l.StartsWith("License: ", StringComparison.Ordinal));
            yield return new LicensedComponent(nameAndVersion[0], nameAndVersion[1], license, copyright, url);
        }
    }

    /// <summary>
    /// A text cut at its blank lines, for a list that only builds the
    /// paragraphs on screen. Line breaks inside a paragraph are kept (lists,
    /// indented clauses). The rules of the plain text ("=====", "-----")
    /// are dropped: a single line next to one is a heading.
    /// </summary>
    public static List<LegalParagraph> Paragraphs(string text)
    {
        var paragraphs = new List<LegalParagraph>();
        var lines = new List<string>();
        bool startsAfterRule = false;

        void Close(bool nextToRule)
        {
            if (lines.Count > 0)
                paragraphs.Add(new LegalParagraph(string.Join('\n', lines), lines.Count == 1 && (startsAfterRule || nextToRule)));
            lines.Clear();
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (IsRule(line))
            {
                Close(nextToRule: true);
                startsAfterRule = true;
            }
            else if (line.Length == 0)
            {
                Close(nextToRule: false);
                startsAfterRule = false;
            }
            else
            {
                lines.Add(line);
            }
        }
        Close(nextToRule: false);
        return paragraphs;
    }

    private static bool IsRule(string line) =>
        line.Length >= 10 && line.All(c => c is '=' or '-' or '_' or '*');
}

/// <summary>A paragraph of a legal text; a heading is shown larger.</summary>
public sealed record LegalParagraph(string Text, bool IsHeading);

/// <summary>The terms of use: common header, then the same terms in each language.</summary>
public sealed record TermsOfUse(string Header, IReadOnlyList<LegalParagraph> French, IReadOnlyList<LegalParagraph> English);

/// <summary>One line of the components list.</summary>
public sealed record LicensedComponent(string Name, string Version, string License, string? Copyright, string? Url);

/// <summary>
/// A section of the notices, in English as in the file: "MIT License",
/// "License of X", "Third-party notices bundled with X, Y".
/// </summary>
public sealed record NoticeSection(string Title, IReadOnlyList<LegalParagraph> Paragraphs);

public sealed record ThirdPartyNoticesText(IReadOnlyList<LicensedComponent> Components, IReadOnlyList<NoticeSection> Sections);
