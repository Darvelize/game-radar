using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameRadar.App.ViewModels;
using GameRadar.App.Views;

namespace GameRadar.App.Tests;

/// <summary>
/// The legal texts of Settings › About: the terms of use and the licenses of
/// the open-source components, which must come with every copy of the exe.
/// </summary>
public sealed class LegalDocumentsTests : AppTest
{
    [Theory]
    [InlineData(LegalDocuments.Terms)]
    [InlineData(LegalDocuments.ThirdPartyNotices)]
    public void Each_text_is_inside_the_app(string name)
    {
        Assert.StartsWith("GameRadar", LegalDocuments.Read(name));
    }

    [Fact]
    public void A_text_is_cut_at_its_blank_lines_and_its_rules_become_headings()
    {
        var paragraphs = LegalDocuments.Paragraphs("GameRadar\n==========\n\n1. Clause\n   detail\n\n\nEnd");

        Assert.Equal(
            [new LegalParagraph("GameRadar", true), new LegalParagraph("1. Clause\n   detail", false), new LegalParagraph("End", false)],
            paragraphs);
    }

    [Fact]
    public void The_terms_are_cut_in_one_part_per_language()
    {
        var terms = LegalDocuments.ReadTerms();

        Assert.Contains("Copyright", terms.Header);
        Assert.Contains(terms.French, p => p.Text.StartsWith("5. Absence de garantie\n", StringComparison.Ordinal));
        Assert.Contains(terms.English, p => p.Text.StartsWith("5. No warranty\n", StringComparison.Ordinal));
        // Each tab holds its own language only.
        Assert.DoesNotContain(terms.French, p => p.Text.Contains("No warranty", StringComparison.Ordinal));
        Assert.DoesNotContain(terms.English, p => p.IsHeading);
    }

    [Fact]
    public void The_notices_are_a_list_of_components_then_sections()
    {
        var notices = LegalDocuments.ReadThirdPartyNotices();

        var avalonia = Assert.Single(notices.Components, c => c.Name == "Avalonia");
        Assert.Equal("MIT", avalonia.License);
        Assert.Contains("AvaloniaUI", avalonia.Copyright);
        Assert.StartsWith("https://", avalonia.Url);
        Assert.Contains(notices.Components, c => c.Name == "Microsoft.NETCore.App.Runtime.win-x64");
        // The license texts and the notices of what a component bundles.
        Assert.Contains(notices.Sections, s => s.Title == "MIT License");
        Assert.Contains(notices.Sections, s => s.Title.StartsWith("License of Avalonia.Angle", StringComparison.Ordinal));
        Assert.Contains(notices.Sections, s => s.Title.StartsWith("Third-party notices bundled with", StringComparison.Ordinal)
                                               && s.Title.Contains("SkiaSharp", StringComparison.Ordinal));
        Assert.All(notices.Sections, s => Assert.NotEmpty(s.Paragraphs));
    }

    [Theory]
    [InlineData("MIT License", "Texte de la licence MIT")]
    [InlineData("License of Avalonia.Angle.Windows.Natives 2.1.2", "Texte de la licence Avalonia.Angle.Windows.Natives")]
    [InlineData("Third-party notices bundled with HarfBuzzSharp 8.3, SkiaSharp 3.1", "Composants inclus dans HarfBuzzSharp, SkiaSharp")]
    public void A_section_title_is_shown_in_the_language_of_the_app(string title, string shown)
    {
        Assert.Equal(shown, NoticeSectionRow.Label(title));
    }

    [AvaloniaFact]
    public void The_terms_open_on_the_tab_of_the_language_of_the_app()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.OpenSettingsCommand.Execute(SettingsTab.About);

        vm.Legal.OpenTermsCommand.Execute(null);
        Assert.Equal(AppPage.Terms, vm.CurrentPage);
        Assert.Equal(0, vm.Legal.TermsTabIndex);
        TestCulture.In("en-GB", () => vm.Legal.OpenTermsCommand.Execute(null));
        Assert.Equal(1, vm.Legal.TermsTabIndex);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(AppPage.Settings, vm.CurrentPage);
        Assert.Equal(SettingsTab.About, vm.CurrentSettingsTab);
    }

    [AvaloniaFact]
    public void The_licenses_open_with_every_section_folded()
    {
        var vm = SampleData.Empty(TempFolder);
        vm.Legal.OpenThirdPartyNoticesCommand.Execute(null);
        vm.Legal.NoticeSections[0].IsExpanded = true;

        vm.GoBackCommand.Execute(null);
        vm.Legal.OpenThirdPartyNoticesCommand.Execute(null);

        Assert.Equal(AppPage.Licenses, vm.CurrentPage);
        Assert.All(vm.Legal.NoticeSections, s => Assert.False(s.IsExpanded));
    }

    [AvaloniaFact]
    public void The_licenses_page_only_builds_the_paragraphs_on_screen()
    {
        var vm = SampleData.Empty(TempFolder);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.Legal.OpenThirdPartyNoticesCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        // Folded: no paragraph built.
        Assert.Empty(Page(window).GetVisualDescendants().OfType<SelectableTextBlock>());

        // The .NET notices unfolded (thousands of paragraphs): a handful built.
        var dotnet = vm.Legal.NoticeSections.Single(s => s.Title.Contains(".NETCore", StringComparison.Ordinal));
        dotnet.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        int built = Page(window).GetVisualDescendants().OfType<SelectableTextBlock>().Count();
        Assert.InRange(built, 1, dotnet.Paragraphs.Count / 10);
        window.Close();
    }

    [AvaloniaFact]
    public void The_licenses_page_opens_at_its_top()
    {
        var vm = SampleData.Empty(TempFolder);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.Legal.OpenThirdPartyNoticesCommand.Execute(null);
        vm.Legal.NoticeSections[^1].IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var scroller = Page(window).GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Offset = new Vector(0, 2000);

        vm.GoBackCommand.Execute(null);
        vm.Legal.OpenThirdPartyNoticesCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, scroller.Offset.Y);
        window.Close();
    }

    [Fact]
    public void The_third_party_notices_name_every_shipped_package()
    {
        // After a package or SDK update: run tools\Update-ThirdPartyNotices.ps1.
        string notices = LegalDocuments.Read(LegalDocuments.ThirdPartyNotices);

        var shipped = ShippedPackages().ToList();
        var missing = shipped.Where(p => !notices.Contains(p, StringComparison.Ordinal)).ToList();

        // The walk really finds the exe's content (not an empty list that proves nothing).
        Assert.Contains(shipped, p => p.StartsWith("Avalonia ", StringComparison.Ordinal));
        Assert.Contains(shipped, p => p.StartsWith("SkiaSharp.NativeAssets.Win32 ", StringComparison.Ordinal));
        Assert.DoesNotContain(shipped, p => p.Contains(".Linux ", StringComparison.Ordinal));
        Assert.True(missing.Count == 0,
            "Missing from legal/THIRD-PARTY-NOTICES.txt (run tools/Update-ThirdPartyNotices.ps1): " + string.Join(", ", missing));
        Assert.Contains("Microsoft.NETCore.App.Runtime.win-x64 ", notices);
    }

    private static LicensesView Page(Window window) =>
        window.GetVisualDescendants().OfType<LicensesView>().Single();

    // "Id version" of every package the Windows exe contains, from the tests'
    // own dependency file: everything reachable from the app ("GameRadar")
    // with a managed or native file (Linux/macOS natives and build-only
    // packages are not in the Windows exe). Same rule as the script.
    private static IEnumerable<string> ShippedPackages()
    {
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GameRadar.App.Tests.deps.json")));
        var target = deps.RootElement.GetProperty("targets").EnumerateObject().First().Value;
        var libraries = deps.RootElement.GetProperty("libraries");

        var seen = new HashSet<string>();
        var toVisit = new Stack<string>();
        toVisit.Push(target.EnumerateObject().First(l => l.Name.StartsWith("GameRadar/", StringComparison.Ordinal)).Name);
        while (toVisit.Count > 0)
        {
            string key = toVisit.Pop();
            if (!seen.Add(key) || !target.TryGetProperty(key, out var library))
                continue;
            if (library.TryGetProperty("dependencies", out var dependencies))
                foreach (var dependency in dependencies.EnumerateObject())
                    toVisit.Push($"{dependency.Name}/{dependency.Value.GetString()}");

            bool isPackage = libraries.GetProperty(key).GetProperty("type").GetString() == "package";
            if (isPackage && IsShipped(library))
                yield return key.Replace('/', ' ');
        }
    }

    private static bool IsShipped(JsonElement library) =>
        library.TryGetProperty("runtime", out _)
        || library.TryGetProperty("native", out _)
        || (library.TryGetProperty("runtimeTargets", out var targets)
            && targets.EnumerateObject().Any(t => t.Value.GetProperty("rid").GetString() == "win-x64"));
}
