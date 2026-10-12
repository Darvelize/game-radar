using GameRadar.Core.Updates;

namespace GameRadar.Core.Tests.Updates;

public class ChangelogTests
{
    private const string Sample = """
        # Nouveautés de GameRadar

        Introduction ignorée, même avec - un tiret.

        ## 1.10.0

        - Pas encore publiée : pas de date.

        ## 1.2.0 — 2026-10-12

        - Première nouveauté.
        - Deuxième nouveauté, écrite
          sur deux lignes.

        ## Notes internes

        - Ignorée : ce titre n'est pas une version.

        ## 1.1.0 - 2026-09-29
        - Tiret simple accepté.
        """;

    [Fact]
    public void Parse_ReadsVersionsFromNewestToOldest()
    {
        var entries = Changelog.Parse(Sample);

        // Numbers, not text: 1.10.0 is newer than 1.2.0.
        Assert.Equal(["1.10.0", "1.2.0", "1.1.0"], entries.Select(e => e.Version.ToString()));
    }

    [Fact]
    public void Parse_ReadsDatesOnlyForReleasedVersions()
    {
        var entries = Changelog.Parse(Sample);

        Assert.Null(entries[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 12), entries[1].Date);
        Assert.Equal(new DateOnly(2026, 9, 29), entries[2].Date);
    }

    [Fact]
    public void Parse_JoinsWrappedItemsAndIgnoresOtherSections()
    {
        var entry = Changelog.Parse(Sample).Single(e => e.Version == new Version(1, 2, 0));

        Assert.Equal(["Première nouveauté.", "Deuxième nouveauté, écrite sur deux lignes."], entry.Items);
    }

    [Fact]
    public void Parse_AcceptsWindowsLineEndings()
    {
        var entries = Changelog.Parse("## 1.2.0\r\n\r\n- Une nouveauté.\r\n");

        Assert.Equal(["Une nouveauté."], Assert.Single(entries).Items);
    }

    [Theory]
    // Updated from 1.1.0 to 1.2.0: only 1.2.0 is new.
    [InlineData("1.1.0", "1.2.0.0", new[] { "1.2.0" })]
    // Skipped a version: both are new.
    [InlineData("1.0.0", "1.2.0.0", new[] { "1.2.0", "1.1.0" })]
    // Nothing new since last time.
    [InlineData("1.2.0", "1.2.0.0", new string[0])]
    // Build of 1.9.0: the notes of 1.10.0, still in preparation, are not shown.
    [InlineData("1.0.0", "1.9.0.0", new[] { "1.2.0", "1.1.0" })]
    public void Unseen_KeepsVersionsBetweenLastSeenAndCurrent(string lastSeen, string current, string[] expected)
    {
        var unseen = Changelog.Unseen(Changelog.Parse(Sample), Version.Parse(lastSeen), Version.Parse(current));

        Assert.Equal(expected, unseen.Select(e => e.Version.ToString()));
    }

    [Fact]
    public void TheRealChangelog_IsReadable()
    {
        // The file shipped in the app: every version has news, and every
        // version but the newest one has its release date.
        string path = Path.Combine(RepositoryRoot(), "CHANGELOG.md");

        var entries = Changelog.Parse(File.ReadAllText(path));

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.NotEmpty(e.Items));
        Assert.All(entries.Skip(1), e => Assert.NotNull(e.Date));
    }

    [Fact]
    public void TheEnglishChangelog_TranslatesEveryVersion()
    {
        // A note forgotten on one side would show in one language only.
        string root = RepositoryRoot();
        var french = Changelog.Parse(File.ReadAllText(Path.Combine(root, "CHANGELOG.md")));
        var english = Changelog.Parse(File.ReadAllText(Path.Combine(root, "CHANGELOG.en.md")));

        Assert.Equal(french.Select(e => (e.Version, e.Date, e.Items.Count)), english.Select(e => (e.Version, e.Date, e.Items.Count)));
        // Same parts, each with as many lines (their titles are translated).
        Assert.Equal(
            french.Select(e => string.Join(",", e.Sections.Select(s => s.Items.Count))),
            english.Select(e => string.Join(",", e.Sections.Select(s => s.Items.Count))));
    }

    [Fact]
    public void Parse_GroupsTheItemsOfAVersionByPart()
    {
        var entry = Changelog.Parse("""
            ## 1.3.0

            ### Évolutions

            - Une nouveauté.
            - Une autre,
              sur deux lignes.

            ### Rien à dire

            ### Corrections

            - Un bug corrigé.

            ## 1.2.0

            - Une ancienne version, sans parties.
            """);

        var latest = entry[0];
        // An empty part says nothing: left out.
        Assert.Equal(["Évolutions", "Corrections"], latest.Sections.Select(s => s.Title));
        Assert.Equal(["Une nouveauté.", "Une autre, sur deux lignes."], latest.Sections[0].Items);
        Assert.Equal(3, latest.Items.Count);
        // Written before the parts: a single part without a title.
        Assert.Null(Assert.Single(entry[1].Sections).Title);
    }

    [Fact]
    public void The_release_notes_of_1_2_0_have_their_improvements_and_fixes()
    {
        var entry = Changelog.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "CHANGELOG.md")))
            .Single(e => e.Version == new Version(1, 2, 0));

        Assert.Equal(["Évolutions", "Corrections"], entry.Sections.Select(s => s.Title));
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "GameRadar.sln")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException("GameRadar.sln not found above the test folder.");
    }
}
