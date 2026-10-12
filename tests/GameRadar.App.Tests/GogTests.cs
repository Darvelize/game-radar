using System.Net;
using System.Text;
using Avalonia.Headless.XUnit;

namespace GameRadar.App.Tests;

/// <summary>
/// The GOG profile in the app: connected by its name (public, no secret), its
/// history among the others, its badge. GOG answers what the fake network says.
/// </summary>
public sealed class GogTests : AppTest
{
    // One page: a game GOG Galaxy followed, and one only owned.
    private const string ProfilePage = """
        { "page": 1, "limit": 50, "pages": 1, "_embedded": { "items": [
            { "game": { "id": "1207658930", "title": "Space Heroes", "achievementSupport": true },
              "stats": { "1": { "playtime": 600, "lastSession": "2026-09-30T20:00:00+00:00", "achievementsPercentage": 100 } } },
            { "game": { "id": "1207658931", "title": "Owned Quest", "achievementSupport": false }, "stats": [] } ] } }
        """;

    [AvaloniaFact]
    public async Task Connecting_a_public_profile_shows_its_history_and_remembers_the_name()
    {
        var gog = new FakeGog(HttpStatusCode.OK);
        var vm = SampleData.Empty(TempFolder, network: gog);
        vm.Accounts.GogProfileInput = "https://www.gog.com/fr/u/Radar_Player/games";

        await vm.Accounts.ConnectGogCommand.ExecuteAsync(null);

        Assert.Equal("/u/Radar_Player/games/stats", gog.Asked!.AbsolutePath);
        Assert.True(vm.Accounts.GogSync.IsConnected);
        Assert.Equal(2, vm.Accounts.GogSync.History!.Games.Count);
        var row = vm.History.Games.Single(r => r.Name == "Space Heroes");
        Assert.True(row.IsGog);
        Assert.True(row.IsMastered);
        // GOG only gives a percentage: no "0 succès".
        Assert.Equal("100 % des succès · 10 h", row.Details.Single().AchievementsLabel.Replace(' ', ' ').Replace(' ', ' '));
        Assert.Equal("Radar_Player", (await SavedSettingsAsync(vm)).GogUser);
    }

    [AvaloniaFact]
    public async Task A_profile_that_is_not_public_is_explained_and_not_remembered()
    {
        var vm = SampleData.Empty(TempFolder, network: new FakeGog(HttpStatusCode.Forbidden));
        vm.Accounts.GogProfileInput = "Radar_Player";

        await vm.Accounts.ConnectGogCommand.ExecuteAsync(null);

        Assert.False(vm.Accounts.GogSync.IsConnected);
        Assert.Contains("n'est pas public", vm.Notices.Message);
        Assert.Null((await SavedSettingsAsync(vm)).GogUser);
    }

    [AvaloniaFact]
    public async Task Something_that_is_not_a_profile_is_refused_before_any_request()
    {
        var gog = new FakeGog(HttpStatusCode.OK);
        var vm = SampleData.Empty(TempFolder, network: gog);
        vm.Accounts.GogProfileInput = "https://www.gog.com/en/game/space_heroes";

        await vm.Accounts.ConnectGogCommand.ExecuteAsync(null);

        Assert.Null(gog.Asked);
        Assert.False(vm.Accounts.GogSync.IsConnected);
        Assert.StartsWith("Colle ton nom de profil GOG", vm.Notices.Message);
    }

    [AvaloniaFact]
    public async Task Disconnecting_forgets_the_name_and_keeps_the_history_shown()
    {
        var vm = SampleData.Empty(TempFolder, network: new FakeGog(HttpStatusCode.OK));
        vm.Accounts.GogProfileInput = "Radar_Player";
        await vm.Accounts.ConnectGogCommand.ExecuteAsync(null);

        await vm.Accounts.DisconnectGogCommand.ExecuteAsync(null);

        Assert.False(vm.Accounts.GogSync.IsConnected);
        Assert.Null((await SavedSettingsAsync(vm)).GogUser);
        Assert.Contains(vm.History.Games, r => r.Name == "Space Heroes");
    }

    [AvaloniaFact]
    public async Task The_saved_profile_is_connected_at_startup_with_its_cached_history()
    {
        var store = new GameRadar.Core.Settings.SettingsStore(TempFolder);
        var settings = store.Load();
        settings.GogUser = "Radar_Player";
        await store.SaveAsync(settings);
        Directory.CreateDirectory(Path.Combine(TempFolder, "cache"));
        await File.WriteAllTextAsync(Path.Combine(TempFolder, "cache", "gog-history.json"), $"[{ProfilePage}]");
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());

        await vm.InitializeAsync();

        Assert.True(vm.Accounts.GogSync.IsConnected);
        Assert.True(vm.Accounts.GogSync.ShowSync);
        Assert.Contains(vm.History.Games, r => r.Name == "Owned Quest" && r.IsGog);
    }

    /// <summary>GOG's profile pages answer with the given status; anything else 404.</summary>
    private sealed class FakeGog(HttpStatusCode status) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "www.gog.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            Asked = uri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? ProfilePage : "", Encoding.UTF8, "application/hal+json")
            });
        }
    }
}
