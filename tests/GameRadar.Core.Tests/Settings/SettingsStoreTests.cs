using GameRadar.Core.Models;
using GameRadar.Core.Settings;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Settings;

public class SettingsStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string SettingsFile => Path.Combine(_folder.Path, "settings.json");

    [Fact]
    public void Load_GivesDefaultsWhenFileIsMissing()
    {
        var settings = new SettingsStore(_folder.Path).Load();

        Assert.Empty(settings.ExcludedFromTaste);
    }

    [Fact]
    public async Task The_PS_Plus_Essential_plan_is_saved_by_its_name()
    {
        await new SettingsStore(_folder.Path).SaveAsync(new AppSettings { PsPlusPlan = PsPlusTier.Essential });

        Assert.Contains("\"Essential\"", File.ReadAllText(SettingsFile));
        Assert.Equal(PsPlusTier.Essential, new SettingsStore(_folder.Path).Load().PsPlusPlan);
    }

    [Fact]
    public void Load_GivesDefaultsWhenFileIsCorrupted()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(SettingsFile, "{ not json");

        Assert.Empty(new SettingsStore(_folder.Path).Load().ExcludedFromTaste);
    }

    [Fact]
    public async Task A_corrupted_file_is_put_aside_before_the_next_save_replaces_it()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(SettingsFile, "{ not json");
        var store = new SettingsStore(_folder.Path);
        var settings = store.Load();

        await store.SaveAsync(settings);

        // The player's choices may still be in it: kept, not overwritten.
        Assert.Equal("{ not json", File.ReadAllText(SettingsFile + ".bad"));
        Assert.NotNull(new SettingsStore(_folder.Path).Load());
    }

    [Fact]
    public async Task A_null_list_in_the_file_reads_as_an_empty_one()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(SettingsFile, """
            { "ExcludedFromTaste": null, "IncludedInTaste": null, "MarkedCompleted": null,
              "NotInterested": null, "ManualGames": null, "AddedTags": null }
            """);
        var store = new SettingsStore(_folder.Path);

        var settings = store.Load();

        Assert.Empty(settings.ExcludedFromTaste);
        Assert.Empty(settings.IncludedInTaste);
        Assert.Empty(settings.MarkedCompleted);
        Assert.Empty(settings.NotInterested);
        Assert.Empty(settings.ManualGames);
        Assert.Empty(settings.AddedTags);
        // And it saves again (Clone copies every list).
        await store.SaveAsync(settings.Clone());
    }

    [Fact]
    public async Task A_null_tag_list_in_the_file_is_dropped()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(SettingsFile, """{ "AddedTags": { "hades": null, "celeste": [ 3871 ] } }""");
        var store = new SettingsStore(_folder.Path);

        var settings = store.Load();

        Assert.Equal(["celeste"], settings.AddedTags.Keys);
        // And it saves again (Clone copies every list).
        await store.SaveAsync(settings.Clone());
    }

    [Fact]
    public async Task EraseAll_also_erases_a_corrupted_file_put_aside()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(SettingsFile, "{ not json");
        var store = new SettingsStore(_folder.Path);
        await store.SaveAsync(store.Load());

        store.EraseAll();

        Assert.False(File.Exists(SettingsFile + ".bad"));
    }

    [Fact]
    public async Task Save_KeepsTheGamePassPlanAsTextAndTheFilter()
    {
        var store = new SettingsStore(_folder.Path);
        var settings = store.Load();
        settings.GamePassPlan = GamePassTier.Premium;
        settings.ShowSteam = false;

        await store.SaveAsync(settings.Clone());

        // Readable in the file, and read back as saved.
        Assert.Contains("\"Premium\"", File.ReadAllText(SettingsFile));
        var reloaded = new SettingsStore(_folder.Path).Load();
        Assert.Equal(GamePassTier.Premium, reloaded.GamePassPlan);
        Assert.True(reloaded.ShowGamePass);
        Assert.False(reloaded.ShowSteam);
    }

    [Fact]
    public async Task Save_KeepsThePsPlusPlanAsTextAndTheFilter()
    {
        var store = new SettingsStore(_folder.Path);
        var settings = store.Load();
        settings.PsPlusPlan = PsPlusTier.Premium;
        settings.ShowPsPlus = false;

        await store.SaveAsync(settings.Clone());

        var reloaded = new SettingsStore(_folder.Path).Load();
        Assert.Equal(PsPlusTier.Premium, reloaded.PsPlusPlan);
        Assert.False(reloaded.ShowPsPlus);
        // The other sources stay as they were.
        Assert.True(reloaded.ShowGamePass && reloaded.ShowSteam);
    }

    [Fact]
    public void Load_DefaultsToNoPsPlusPlan()
    {
        // Before PS Plus existed (an older settings.json has neither field): no subscription
        // known, so no PS Plus game is shown until the player says which plan they have.
        var settings = new SettingsStore(_folder.Path).Load();

        Assert.Equal(PsPlusTier.None, settings.PsPlusPlan);
        Assert.True(settings.ShowPsPlus);
    }

    [Fact]
    public void Load_DefaultsToUltimateAndEverySource()
    {
        // Before the plans existed: every Game Pass game, and Steam.
        var settings = new SettingsStore(_folder.Path).Load();

        Assert.Equal(GamePassTier.Ultimate, settings.GamePassPlan);
        Assert.True(settings.ShowGamePass && settings.ShowSteam);
    }

    [Fact]
    public void Load_ReadsFileFromOlderVersionWithoutNewFields()
    {
        // Written by an older version: before MarkedCompleted existed, with
        // the OpenXBL key, now unused (unknown fields are ignored).
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(SettingsFile, """{ "OpenXblApiKey": "key", "ExcludedFromTaste": ["Xbox:9"] }""");

        var settings = new SettingsStore(_folder.Path).Load();

        Assert.Equal(["Xbox:9"], settings.ExcludedFromTaste);
        Assert.Empty(settings.MarkedCompleted);
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsEveryField()
    {
        var store = new SettingsStore(_folder.Path);
        var settings = new AppSettings
        {
            ExcludedFromTaste = ["Xbox:1"],
            MarkedCompleted = ["Xbox:2"]
        };

        await store.SaveAsync(settings);
        var loaded = store.Load();

        Assert.Equal(["Xbox:1"], loaded.ExcludedFromTaste);
        Assert.Equal(["Xbox:2"], loaded.MarkedCompleted);
    }

    [Fact]
    public void Clone_CopiesEveryFieldIntoIndependentSets()
    {
        var original = new AppSettings
        {
            ExcludedFromTaste = ["Xbox:1"],
            MarkedCompleted = ["Xbox:2"],
            AddedTags = new() { ["hades"] = [1774] }
        };

        var clone = original.Clone();
        original.ExcludedFromTaste.Add("Xbox:3");
        original.MarkedCompleted.Clear();
        original.AddedTags["hades"].Add(122);

        Assert.Equal(["Xbox:1"], clone.ExcludedFromTaste);
        Assert.Equal(["Xbox:2"], clone.MarkedCompleted);
        Assert.Equal([1774], clone.AddedTags["hades"]);
    }

    [Fact]
    public void Clone_ForgetsNoProperty()
    {
        // A property missing from Clone() is silently never saved. Every
        // property gets a non-default value by reflection, so a new one is
        // checked without touching this test (or fails on an unknown type).
        var original = new AppSettings();
        foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.CanWrite))
        {
            object value = property.PropertyType switch
            {
                var t when t == typeof(HashSet<string>) => new HashSet<string> { "Xbox:42" },
                var t when t == typeof(Dictionary<string, string>) => new Dictionary<string, string> { ["halo"] = "Halo" },
                var t when t == typeof(List<GameRadar.Core.Manual.ManualGame>) => new List<GameRadar.Core.Manual.ManualGame>
                    { new("Hades", GameRadar.Core.Manual.ManualPlatform.Switch, GameRadar.Core.Manual.ManualLevel.Completed, "1145360") },
                var t when t == typeof(Dictionary<string, List<int>>) => new Dictionary<string, List<int>> { ["hades"] = [1774, 122] },
                var t when t == typeof(bool) => !(bool)property.GetValue(original)!,
                var t when t == typeof(bool?) => true,
                var t when t == typeof(string) => "1.2.0",
                var t when t.IsEnum => Enum.GetValues(t).Cast<object>().First(v => !v.Equals(property.GetValue(original))),
                var t => throw new NotSupportedException($"Teach this test a value for {t.Name} ({property.Name}).")
            };
            property.SetValue(original, value);
        }

        var clone = original.Clone();

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(original, TestJsonContext.Default.AppSettings),
            System.Text.Json.JsonSerializer.Serialize(clone, TestJsonContext.Default.AppSettings));
    }
}
