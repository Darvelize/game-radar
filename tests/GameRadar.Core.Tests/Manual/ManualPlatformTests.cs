using System.Text.Json;
using GameRadar.Core.Manual;

namespace GameRadar.Core.Tests.Manual;

public class ManualPlatformTests
{
    [Fact]
    public void AGameSavedBeforeTheFamiliesStillReads()
    {
        // settings.json of 1.3.0: the platform is saved by name.
        var game = JsonSerializer.Deserialize(
            """{ "Title": "Halo 3", "Platform": "OldXbox", "Level": "Completed" }""", TestJsonContext.Default.ManualGame);

        Assert.Equal(ManualPlatform.OldXbox, game!.Platform);
    }

    [Theory]
    [InlineData(ManualPlatform.Android, "Android")]
    [InlineData(ManualPlatform.Ios, "Ios")]
    public void TheMobilePlatformsRoundTripByName(ManualPlatform platform, string name)
    {
        string json = JsonSerializer.Serialize(new ManualGame("Alto's Odyssey", platform, ManualLevel.PlayedALot), TestJsonContext.Default.ManualGame);

        Assert.Contains($"\"Platform\":\"{name}\"", json);
        Assert.Equal(platform, JsonSerializer.Deserialize(json, TestJsonContext.Default.ManualGame)!.Platform);
    }
}

public class ManualGameGenresTests
{
    [Fact]
    public void A_game_saved_before_the_genres_still_reads_without_any()
    {
        // settings.json of 1.3.0: no "Genres" at all.
        var game = JsonSerializer.Deserialize(
            """{ "Title": "Halo 3", "Platform": "OldXbox", "Level": "Completed", "SteamAppId": null }""", TestJsonContext.Default.ManualGame);

        Assert.Null(game!.Genres);
    }

    [Fact]
    public void The_genres_of_a_game_round_trip_in_the_settings()
    {
        var game = new ManualGame("Astro Bot", ManualPlatform.PlayStation, ManualLevel.Mastered, Genres: ["Platformer"]);

        var read = JsonSerializer.Deserialize(JsonSerializer.Serialize(game, TestJsonContext.Default.ManualGame), TestJsonContext.Default.ManualGame)!;

        Assert.Equal(["Platformer"], read.Genres);
        Assert.Equal(game.Id, read.Id);
    }

    [Fact]
    public void Only_the_games_with_genres_give_platform_genres_keyed_like_the_played_game()
    {
        var astro = new ManualGame("Astro Bot", ManualPlatform.PlayStation, ManualLevel.Mastered, Genres: ["Platformer"]);
        var typed = new ManualGame("Golden Sun", ManualPlatform.Other, ManualLevel.Completed);
        var empty = new ManualGame("Nothing", ManualPlatform.Other, ManualLevel.Completed, Genres: []);

        var genres = ManualGames.PlatformGenres([astro, typed, empty]);

        // The key the recommendations use for the platform genres of a played game.
        var entry = Assert.Single(genres);
        Assert.Equal(ManualGames.ToPlayed(astro).Key, entry.Key);
        Assert.Equal(["Platformer"], entry.Value);
    }
}
