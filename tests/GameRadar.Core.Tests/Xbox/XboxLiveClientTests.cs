using System.Text.Json;
using GameRadar.Core.Models;
using GameRadar.Core.Xbox;

namespace GameRadar.Core.Tests.Xbox;

public class XboxLiveClientTests
{
    // History cached when the app used OpenXBL: the titlehub payload wrapped
    // in { content, code }. Such caches must still load.
    private const string Json = """
        {
          "content": {
            "xuid": "123",
            "titles": [
              {
                "titleId": "1", "pfn": "Pub.Recent_abc", "name": " Recent game ", "type": "Game",
                "devices": ["PC", "XboxSeries"],
                "achievement": { "currentAchievements": 33, "totalAchievements": 0, "totalGamerscore": 1000, "progressPercentage": 61 },
                "titleHistory": { "lastTimePlayed": "2026-09-22T15:00:00Z" }
              },
              {
                "titleId": "2", "name": "Mastered game", "type": "Game",
                "devices": ["XboxOne"],
                "achievement": { "currentAchievements": 50, "totalAchievements": 50, "totalGamerscore": 1000, "progressPercentage": 100 },
                "titleHistory": { "lastTimePlayed": "2026-01-01T00:00:00Z" }
              },
              {
                "titleId": "3", "name": "Steam game", "type": "Game",
                "devices": ["Win32"],
                "achievement": { "currentAchievements": 0, "totalAchievements": 0, "totalGamerscore": 0, "progressPercentage": 0 },
                "titleHistory": { "lastTimePlayed": "2026-05-01T00:00:00Z" }
              },
              {
                "titleId": "4", "name": "Netflix", "type": "App", "devices": ["XboxSeries"]
              }
            ]
          },
          "code": 200
        }
        """;

    private static readonly IReadOnlyList<PlayedGame> Games = XboxLiveClient.ParseJson(Json);

    [Fact]
    public void ParseJson_ReadsOldEnvelopeKeepsGamesOnlyMostRecentFirst()
    {
        Assert.Equal(["1", "2"], Games.Select(g => g.PlatformId));
    }

    [Fact]
    public void ParseJson_TreatsZeroTotalAsUnknownButKeepsAchievementsFromGamerscore()
    {
        var recent = Games.Single(g => g.PlatformId == "1");

        Assert.Null(recent.TotalAchievements);
        Assert.True(recent.HasAchievements);
        Assert.Equal(61, recent.ProgressPercentage);
        Assert.Equal("Recent game", recent.Name);
        Assert.Equal("Pub.Recent_abc", recent.PackageFamilyName);
        Assert.Equal("Xbox:1", recent.Key);
    }

    [Fact]
    public void ParseJson_DetectsMasteredGame()
    {
        Assert.Equal(PlayStatus.Mastered, Games.Single(g => g.PlatformId == "2").Status);
    }

    [Fact]
    public void ParseJson_LeavesOutTheGamesFromOtherStores()
    {
        // Only seen being launched by the Xbox PC app (device "Win32" alone):
        // no achievements, no play time, nothing about taste (choice of the user).
        Assert.DoesNotContain(Games, g => g.PlatformId == "3");
    }

    [Fact]
    public void ParseJson_KeepsAGameFromAnotherStoreThatEarnsXboxAchievements()
    {
        // A Microsoft game bought on Steam, which awards gamerscore: its
        // progress says something about the taste, unlike a bare launch.
        var games = XboxLiveClient.ParseJson("""
            { "titles": [ { "titleId": "5", "name": "Halo MCC", "type": "Game", "devices": ["Win32"],
              "achievement": { "currentAchievements": 40, "totalAchievements": 0, "totalGamerscore": 7000, "progressPercentage": 12 } } ] }
            """);

        Assert.Equal("5", Assert.Single(games).PlatformId);
    }

    [Theory]
    // Its 16:9 picture first, else the one Xbox shows; in https.
    [InlineData("""[ { "url": "http://store-images.s-microsoft.com/image/box", "type": "BoxArt" }, { "url": "http://store-images.s-microsoft.com/image/hero", "type": "SuperHeroArt" } ]""",
        "https://store-images.s-microsoft.com/image/hero")]
    [InlineData("null", "https://store-images.s-microsoft.com/image/display")]
    public void ParseJson_KeepsThePictureOfAGame(string images, string expected)
    {
        var game = Assert.Single(XboxLiveClient.ParseJson($$"""
            { "titles": [ { "titleId": "7", "name": "Metal Slug Tactics", "type": "Game", "devices": ["XboxSeries"],
                            "displayImage": "http://store-images.s-microsoft.com/image/display", "images": {{images}} } ] }
            """));

        Assert.Equal(expected, game.ImageUrl);
    }

    [Fact]
    public void ParseJson_ReadsTitlehubAnswerWithoutEnvelope()
    {
        // Direct answer of titlehub: no "content" wrapper.
        var games = XboxLiveClient.ParseJson("""
            {
              "xuid": "123",
              "titles": [
                {
                  "titleId": "7", "name": "Direct game", "type": "Game", "devices": ["XboxSeries"],
                  "achievement": { "currentAchievements": 10, "totalAchievements": 10, "totalGamerscore": 500, "progressPercentage": 100 },
                  "titleHistory": { "lastTimePlayed": "2026-09-01T10:00:00Z" }
                }
              ]
            }
            """);

        var game = Assert.Single(games);
        Assert.Equal("Xbox:7", game.Key);
        Assert.Equal(PlayStatus.Mastered, game.Status);
    }

    [Fact]
    public void ParseJson_ReturnsEmptyWithoutContent()
    {
        Assert.Empty(XboxLiveClient.ParseJson("""{ "code": 200 }"""));
    }

    [Fact]
    public void ParseFetchedJson_AnAnswerWithoutTitles_Throws()
    {
        // Saved, it would replace the cached history with an empty one, without a word.
        Assert.Throws<XboxLiveException>(() => XboxLiveClient.ParseFetchedJson("""{ "code": 200 }"""));
        Assert.Empty(XboxLiveClient.ParseFetchedJson("""{ "titles": [] }"""));
    }

    [Fact]
    public void ParseJson_ThrowsOnInvalidJson()
    {
        Assert.ThrowsAny<JsonException>(() => XboxLiveClient.ParseJson("{ not json"));
    }
}
