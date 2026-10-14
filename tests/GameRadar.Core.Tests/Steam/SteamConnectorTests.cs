using System.Net;
using System.Text;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Settings;
using GameRadar.Core.Steam;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Steam;

public class SteamConnectorTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeSteamHandler _handler = new();
    private readonly HttpClient _http;

    public SteamConnectorTests() => _http = new HttpClient(_handler);

    public void Dispose()
    {
        _http.Dispose();
        _folder.Dispose();
    }

    // ---------- Profile ----------

    [Theory]
    [InlineData("https://steamcommunity.com/profiles/76561198000000001/", "76561198000000001", null)]
    [InlineData("76561198000000001", "76561198000000001", null)]
    [InlineData("https://steamcommunity.com/id/radarplayer", null, "radarplayer")]
    [InlineData("radarplayer", null, "radarplayer")]
    // Copied from a subpage or with a query: the profile is still in it.
    [InlineData("https://steamcommunity.com/profiles/76561198000000001/games/?tab=all", "76561198000000001", null)]
    [InlineData("https://steamcommunity.com/id/radarplayer/?l=french", null, "radarplayer")]
    [InlineData("steamcommunity.com/id/radarplayer/home#top", null, "radarplayer")]
    [InlineData("https://example.com/whatever", null, null)]
    public void ParseProfile_AcceptsWhatUsersPaste(string pasted, string? steamId, string? customName)
    {
        Assert.Equal((steamId, customName), SteamClient.ParseProfile(pasted));
    }

    [Fact]
    public async Task Connect_ResolvesCustomNameChecksKeyAndSavesEncrypted()
    {
        var account = new SteamAccount(new SecretStore(_folder.Path));

        await account.ConnectAsync(_http, "good-key", "https://steamcommunity.com/id/radarplayer");

        Assert.True(account.IsConnected);
        Assert.Equal("76561198000000001", account.SteamId);
        Assert.Equal("Radar Player", account.PersonaName);
        // Reloaded from disk, where the key is not readable as such.
        Assert.Equal("76561198000000001", new SteamAccount(new SecretStore(_folder.Path)).SteamId);
        string onDisk = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_folder.Path, "steam-account.bin")));
        Assert.DoesNotContain("good-key", onDisk);
    }

    [Fact]
    public async Task Connect_ExplainsARefusedKey()
    {
        var account = new SteamAccount(new SecretStore(_folder.Path));

        var error = await Assert.ThrowsAsync<SteamException>(
            () => account.ConnectAsync(_http, "bad-key", "76561198000000001"));

        Assert.Contains("Clé API Steam refusée", error.Message);
        Assert.False(account.IsConnected);
    }

    [Fact]
    public async Task Connect_RefusesASteamIdNoProfileHas()
    {
        var account = new SteamAccount(new SecretStore(_folder.Path));

        // A typo in the number: Steam answers, but with no player.
        var error = await Assert.ThrowsAsync<SteamException>(
            () => account.ConnectAsync(_http, "good-key", FakeSteamHandler.UnknownSteamId));

        Assert.Contains(FakeSteamHandler.UnknownSteamId, error.Message);
        Assert.False(account.IsConnected);
    }

    // ---------- Library ----------

    [Fact]
    public void ParseLibrary_ExplainsAPrivateProfile()
    {
        var error = Assert.Throws<SteamException>(() => SteamClient.ParseLibrary("""{ "response": {} }"""));

        Assert.Contains("privée", error.Message);
    }

    [Fact]
    public void ParseAchievements_CountsUnlockedOutOfTotal()
    {
        var achievements = SteamClient.ParseAchievements("""
            { "playerstats": { "success": true, "achievements": [ { "achieved": 1 }, { "achieved": 0 }, { "achieved": 1 }, { "achieved": 1 } ] } }
            """);

        Assert.Equal(new SteamAchievements(3, 4), achievements);
    }

    [Fact]
    public async Task Refresh_AsksAchievementsOnlyForPlayedGamesWithStats()
    {
        var source = new SteamHistorySource(new CacheStore(_folder.Path));

        var history = await source.RefreshAsync(new SteamClient(_http, "good-key"), "76561198000000001");

        // Library + 1 game played with stats (not the unplayed one, not the one without stats).
        Assert.Equal(["GetOwnedGames", "GetPlayerAchievements:10"], _handler.Calls);
        var looter = history.Games.Single(g => g.PlatformId == "10");
        Assert.Equal("Steam:10", looter.Key);
        Assert.True(looter.HasAchievements);
        Assert.Equal(75, looter.ProgressPercentage);
        Assert.Equal(1500, looter.PlaytimeMinutes);

        var indie = history.Games.Single(g => g.PlatformId == "20");
        Assert.False(indie.HasAchievements);
        Assert.True(indie.HasEngagementSignal);

        var backlog = history.Games.Single(g => g.PlatformId == "30");
        Assert.True(backlog.NeverPlayed);
        Assert.False(backlog.HasEngagementSignal);
    }

    [Fact]
    public async Task Refresh_ReusesAchievementsOfGamesNotPlayedSince()
    {
        var source = new SteamHistorySource(new CacheStore(_folder.Path));
        var client = new SteamClient(_http, "good-key");
        await source.RefreshAsync(client, "76561198000000001");
        _handler.Calls.Clear();

        await source.RefreshAsync(client, "76561198000000001");
        Assert.Equal(["GetOwnedGames"], _handler.Calls);

        // Played again since: its achievements are asked again.
        _handler.LooterLastPlayed += 3600;
        _handler.Calls.Clear();
        await source.RefreshAsync(client, "76561198000000001");
        Assert.Equal(["GetOwnedGames", "GetPlayerAchievements:10"], _handler.Calls);
    }

    [Fact]
    public async Task Refresh_SkipsAGameWhoseAchievementsFailAndAsksThemAgainNextTime()
    {
        var cache = new CacheStore(_folder.Path);
        var source = new SteamHistorySource(cache);
        var client = new SteamClient(_http, "good-key");
        // Some games answer a server error, every time: the rest of the sync goes on.
        _handler.AchievementsStatus = HttpStatusCode.InternalServerError;

        var history = await source.RefreshAsync(client, "76561198000000001");

        Assert.False(history.Games.Single(g => g.PlatformId == "10").HasAchievements);
        Assert.NotNull(new SteamHistorySource(cache).LoadCached());

        // Not remembered as "no achievements": asked again.
        _handler.AchievementsStatus = HttpStatusCode.OK;
        _handler.Calls.Clear();
        history = await source.RefreshAsync(client, "76561198000000001");
        Assert.Equal(["GetOwnedGames", "GetPlayerAchievements:10"], _handler.Calls);
        Assert.True(history.Games.Single(g => g.PlatformId == "10").HasAchievements);
    }

    [Fact]
    public async Task LoadCached_RebuildsTheHistoryWithoutNetwork()
    {
        var cache = new CacheStore(_folder.Path);
        await new SteamHistorySource(cache).RefreshAsync(new SteamClient(_http, "good-key"), "76561198000000001");
        _handler.Calls.Clear();

        var history = new SteamHistorySource(cache).LoadCached();

        Assert.NotNull(history);
        Assert.Empty(_handler.Calls);
        Assert.Equal(3, history.Games.Count);
        Assert.Equal(PlayStatus.Played, history.Games.Single(g => g.PlatformId == "10").Status);
    }

    /// <summary>Answers like the Steam Web API for a tiny library, and records the calls.</summary>
    private sealed class FakeSteamHandler : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        public long LooterLastPlayed { get; set; } = 1_790_000_000;
        public HttpStatusCode AchievementsStatus { get; set; } = HttpStatusCode.OK;
        public const string UnknownSteamId = "76561198000000099";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string url = request.RequestUri!.ToString();
            if (!url.Contains("key=good-key"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

            string path = request.RequestUri.AbsolutePath;
            HttpResponseMessage response;
            if (path.Contains("ResolveVanityURL"))
                response = Json("""{ "response": { "success": 1, "steamid": "76561198000000001" } }""");
            else if (path.Contains("GetPlayerSummaries"))
                response = url.Contains(UnknownSteamId)
                    ? Json("""{ "response": { "players": [] } }""")
                    : Json("""{ "response": { "players": [ { "personaname": "Radar Player" } ] } }""");
            else if (path.Contains("GetOwnedGames"))
            {
                Calls.Add("GetOwnedGames");
                response = Json($$"""
                    { "response": { "game_count": 4, "games": [
                        { "appid": 10, "name": "Looter", "playtime_forever": 1500, "rtime_last_played": {{LooterLastPlayed}}, "has_community_visible_stats": true },
                        { "appid": 20, "name": "Indie without achievements", "playtime_forever": 300, "rtime_last_played": 1780000000, "has_community_visible_stats": false },
                        { "appid": 30, "name": "Backlog", "playtime_forever": 0, "rtime_last_played": 0, "has_community_visible_stats": true },
                        { "appid": 40, "name": "", "playtime_forever": 10 }
                    ] } }
                    """);
            }
            else if (path.Contains("GetPlayerAchievements"))
            {
                string appId = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["appid"]!;
                Calls.Add("GetPlayerAchievements:" + appId);
                response = AchievementsStatus != HttpStatusCode.OK ? new HttpResponseMessage(AchievementsStatus) : Json("""{ "playerstats": { "success": true, "achievements": [ { "achieved": 1 }, { "achieved": 1 }, { "achieved": 1 }, { "achieved": 0 } ] } }""");
            }
            else
                response = new HttpResponseMessage(HttpStatusCode.NotFound);

            return Task.FromResult(response);
        }
    }
}
