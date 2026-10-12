using GameRadar.Core.Cache;
using GameRadar.Core.Manual;
using GameRadar.Core.Models;
using GameRadar.Core.Settings;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Settings;

/// <summary>
/// The files a player already has were written by version 1.3.0, when the JSON
/// was read by reflection. The generated serializers must read them as they
/// are, and write the same format back (a player may go back to 1.3.0).
/// </summary>
public class JsonFormatCompatibilityTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    // As 1.3.0 wrote it: indented, enums by name, hand-copied from a real file with the titles made up.
    private const string SettingsOf130 = """
        {
          "ExcludedFromTaste": [
            "Xbox:1"
          ],
          "IncludedInTaste": [],
          "MarkedCompleted": [
            "Steam:2"
          ],
          "NotInterested": {
            "hades": "Hades"
          },
          "ManualGames": [
            {
              "Title": "Hades",
              "Platform": "Switch",
              "Level": "Completed",
              "SteamAppId": "1145360",
              "Genres": [
                "Action"
              ]
            }
          ],
          "GamePassPlan": "Premium",
          "ShowGamePass": false,
          "PsPlusPlan": "Extra",
          "ShowPsPlus": true,
          "ShowSteam": true,
          "LastSeenVersion": "1.3.0",
          "Language": "en"
        }
        """;

    [Fact]
    public void Settings_written_by_1_3_0_are_read()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(Path.Combine(_folder.Path, "settings.json"), SettingsOf130);

        var settings = new SettingsStore(_folder.Path).Load();

        Assert.Equal(["Xbox:1"], settings.ExcludedFromTaste);
        Assert.Equal(["Steam:2"], settings.MarkedCompleted);
        Assert.Equal("Hades", settings.NotInterested["hades"]);
        var game = Assert.Single(settings.ManualGames);
        Assert.Equal(new ManualGame("Hades", ManualPlatform.Switch, ManualLevel.Completed, "1145360", ["Action"]).Title, game.Title);
        Assert.Equal(ManualPlatform.Switch, game.Platform);
        Assert.Equal(ManualLevel.Completed, game.Level);
        Assert.Equal("1145360", game.SteamAppId);
        Assert.Equal(["Action"], game.Genres);
        Assert.Equal(GamePassTier.Premium, settings.GamePassPlan);
        Assert.False(settings.ShowGamePass);
        Assert.Equal(PsPlusTier.Extra, settings.PsPlusPlan);
        // Unknown to 1.3.0: the GOG box starts ticked, as the other stores'.
        Assert.True(settings.ShowGog);
        Assert.True(settings.ShowEpic);
        Assert.Empty(settings.AddedTags);
        Assert.Equal("1.3.0", settings.LastSeenVersion);
        Assert.Equal("en", settings.Language);
    }

    [Fact]
    public async Task Settings_are_written_in_the_same_format()
    {
        var store = new SettingsStore(_folder.Path);
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(Path.Combine(_folder.Path, "settings.json"), SettingsOf130);

        await store.SaveAsync(store.Load());

        // Same text as the file read, but for the line endings of the raw string above
        // and the settings added since, with their default (1.3.0 ignores them when read back).
        string expected = SettingsOf130.ReplaceLineEndings("\n")
            .Replace("  ],\n  \"GamePassPlan\"", "  ],\n  \"AddedTags\": {},\n  \"GamePassPlan\"")
            .Replace("  \"ShowSteam\": true,\n", "  \"ShowSteam\": true,\n  \"ShowGog\": true,\n  \"ShowEpic\": true,\n");
        string written = File.ReadAllText(Path.Combine(_folder.Path, "settings.json"));
        Assert.Equal(expected, written.ReplaceLineEndings("\n"));
    }

    // A compact catalog as 1.3.0 cached it: no indentation, numbers for the flag enums, tag ids as keys.
    private const string CatalogOf130 =
        """{"Games":[{"ProductId":"p1","Title":"Hades","XboxTitleId":null,"PackageFamilyName":null,"Genres":["Action"],"OnPc":true,"OnConsole":false,"LeavingSoon":false,"AverageRating":4.5,"RatingCount":10,"Modes":1,"ImageUrl":null,"SteamAppId":"1145360","Tags":{"113":0.5},"ConsoleTiers":0,"PcTiers":3,"PsPlusTiers":0,"PlayStationConceptId":null}],"FetchedAt":"2026-10-01T10:00:00+00:00"}""";

    [Fact]
    public async Task A_catalog_cached_by_1_3_0_is_read_and_written_back_as_it_was()
    {
        var cache = new CacheStore(Path.Combine(_folder.Path, "cache"));
        await cache.SaveAsync("steam-catalog-fr-v3", CatalogOf130);

        var catalog = cache.LoadJson("steam-catalog-fr-v3", CoreJsonContext.Default.GameCatalog);

        var game = Assert.Single(catalog!.Games);
        Assert.Equal("Hades", game.Title);
        Assert.Equal(4.5, game.AverageRating);
        Assert.Equal(0.5, game.Tags![113]);
        Assert.Equal((PlayModes)1, game.Modes);
        Assert.Equal((GamePassTier)3, game.PcTiers);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), catalog.FetchedAt);
        Assert.Equal(CatalogOf130, System.Text.Json.JsonSerializer.Serialize(catalog, CoreJsonContext.Default.GameCatalog));
    }
}
