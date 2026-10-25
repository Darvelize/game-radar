using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;

namespace GameRadar.App.Tests;

/// <summary>
/// The EA history in the app: a token pasted from EA's page (never saved),
/// the played games with their badge. EA answers what the fake network says.
/// </summary>
public sealed class EaTests : AppTest
{
    private const string Token = "QVQxOjIuMDozLjA6NjA6bWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFkZXVwbWFk";
    private const string NewToken = "QVQxOjIuMDozLjA6NjA6bmV3dG9rZW5uZXd0b2tlbm5ld3Rva2VubmV3dG9rZW5uZXd0b2tlbm5ld3Rva2Vu";
    private static readonly string Page = $$"""{"access_token":"{{Token}}","token_type":"Bearer","expires_in":14399}""";

    /// <summary>A fake EA: one game, played and finished at 100 %; or every request refused.</summary>
    private sealed class FakeEa(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        // The lists of every game asked: one per sync.
        public int Syncs { get; private set; }

        /// <summary>A token that expired meanwhile: refused (401).</summary>
        public string? Expired { get; set; }

        /// <summary>Set: the list of every game waits for it (a slow sync); Reached says one waits.</summary>
        public TaskCompletionSource? Hold { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "service-aggregation-layer.juno.ea.com")
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            Requests++;
            if (request.Headers.Authorization is null)
            {
                if (Hold is { } hold)
                {
                    Reached.TrySetResult();
                    await hold.Task.WaitAsync(ct);
                }
                Syncs++;
            }
            // The token refused: only the requests that send it (the list of every game is public).
            if (request.Headers.Authorization is { } sent && (status != HttpStatusCode.OK || sent.Parameter == Expired))
                return new HttpResponseMessage(sent.Parameter == Expired ? HttpStatusCode.Unauthorized : status) { Content = new StringContent("") };

            string query = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!["query"]!.GetValue<string>();
            string json = query.Contains("gameSearch") ? """{"data":{"gameSearch":{"items":[{"slug":"space-heroes"}]}}}"""
                : query.Contains("recentGames") ? """{"data":{"me":{"recentGames":{"items":[{"gameSlug":"space-heroes","lastSessionEndDate":"2023-05-07T15:26:58.000Z","totalPlayTimeSeconds":36000}]}}}}"""
                : query.Contains("games(") ? """{"data":{"games":{"items":[{"slug":"space-heroes","products":{"items":[{"name":"Space Heroes™","originOfferId":"offer-1","baseItem":{"gameType":"BASE_GAME"}}]}}]}}}"""
                : query.Contains("achievements") ? """{"data":{"achievements":[{"achievements":[{"awardCount":1},{"awardCount":1}]}]}}"""
                : """{"data":{"me":{"player":{"psd":"1000123"}}}}""";
            return FakeStores.Json(json);
        }
    }

    [AvaloniaFact]
    public async Task Pasting_the_EA_page_brings_the_played_games_and_clears_the_field()
    {
        var vm = SampleData.Empty(TempFolder, network: new FakeEa());
        vm.Accounts.EaTokenInput = Page;

        await vm.Accounts.UpdateEaCommand.ExecuteAsync(null);

        Assert.Equal("", vm.Accounts.EaTokenInput);
        // The token stays in memory for the session: the general sync takes EA too.
        Assert.True(vm.Accounts.EaSync.IsConnected);
        var row = vm.History.Games.Single(r => r.Name == "Space Heroes™");
        Assert.True(row.IsEa);
        Assert.True(row.IsMastered);
        // Never written to disk: nothing of the session in the settings or the secrets.
        Assert.DoesNotContain(Token, string.Concat(Directory.GetFiles(TempFolder, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}cache{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)));
    }

    [AvaloniaFact]
    public async Task A_token_pasted_during_a_sync_gets_its_own_sync_once_it_ends()
    {
        var ea = new FakeEa();
        var vm = SampleData.Empty(TempFolder, network: ea);
        vm.Accounts.EaTokenInput = Page;
        await vm.Accounts.UpdateEaCommand.ExecuteAsync(null);
        // The general sync, slow on EA.
        ea.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var general = vm.Accounts.RefreshHistoryCommand.ExecuteAsync(null);
        await ea.Reached.Task;

        vm.Accounts.EaTokenInput = Page;
        var update = vm.Accounts.UpdateEaCommand.ExecuteAsync(null);
        ea.Hold.SetResult();
        await Task.WhenAll(general, update);

        // The first sync, the general one, then the new token's: skipped, it never came.
        Assert.Equal(3, ea.Syncs);
    }

    [AvaloniaFact]
    public async Task The_expired_token_of_a_sync_leaves_the_banner_once_the_new_one_works()
    {
        var ea = new FakeEa();
        var vm = SampleData.Empty(TempFolder, network: ea);
        vm.Accounts.EaTokenInput = Page;
        await vm.Accounts.UpdateEaCommand.ExecuteAsync(null);
        // The token expires during a general sync; the player pastes a new one meanwhile.
        ea.Expired = Token;
        ea.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var general = vm.Accounts.RefreshHistoryCommand.ExecuteAsync(null);
        await ea.Reached.Task;

        vm.Accounts.EaTokenInput = Page.Replace(Token, NewToken);
        var update = vm.Accounts.UpdateEaCommand.ExecuteAsync(null);
        ea.Hold.SetResult();
        await Task.WhenAll(general, update);

        Assert.True(vm.Accounts.EaSync.IsConnected);
        Assert.DoesNotContain("expiré", vm.Notices.Message);
    }

    [AvaloniaFact]
    public void The_two_links_open_ea_com_then_the_EA_page_in_the_browser()
    {
        var opened = new List<Uri>();
        var vm = SampleData.Empty(TempFolder, openBrowser: opened.Add);

        vm.Accounts.OpenEaSignInCommand.Execute(null);
        vm.Accounts.OpenEaPageCommand.Execute(null);

        Assert.Equal("https://www.ea.com/", opened[0].ToString());
        Assert.StartsWith("https://accounts.ea.com/connect/auth?client_id=ORIGIN_JS_SDK", opened[1].ToString());
    }

    [AvaloniaFact]
    public async Task Something_else_than_the_EA_page_is_refused_before_any_request()
    {
        var ea = new FakeEa();
        var vm = SampleData.Empty(TempFolder, network: ea);
        vm.Accounts.EaTokenInput = "my password";

        await vm.Accounts.UpdateEaCommand.ExecuteAsync(null);

        Assert.Equal(0, ea.Requests);
        Assert.False(vm.Accounts.EaSync.IsConnected);
        Assert.StartsWith("Colle tout le texte de la page EA", vm.Notices.Message);
        Assert.Equal("", vm.Accounts.EaTokenInput);
    }

    [AvaloniaFact]
    public async Task An_expired_token_is_explained_and_forgotten()
    {
        var vm = SampleData.Empty(TempFolder, network: new FakeEa(HttpStatusCode.Unauthorized));
        vm.Accounts.EaTokenInput = Page;

        await vm.Accounts.UpdateEaCommand.ExecuteAsync(null);

        Assert.False(vm.Accounts.EaSync.IsConnected);
        Assert.Contains("expiré", vm.Notices.Message);
    }

    [AvaloniaFact]
    public async Task The_cached_EA_history_shows_at_startup_without_a_token()
    {
        Directory.CreateDirectory(Path.Combine(TempFolder, "cache"));
        await File.WriteAllTextAsync(Path.Combine(TempFolder, "cache", "ea-history.json"), """
            {"recentGames":[{"data":{"me":{"recentGames":{"items":[{"gameSlug":"space-heroes","lastSessionEndDate":"2023-05-07T15:26:58.000Z","totalPlayTimeSeconds":36000}]}}}}],
             "games":[{"data":{"games":{"items":[{"slug":"space-heroes","products":{"items":[{"name":"Space Heroes™","originOfferId":"offer-1","baseItem":{"gameType":"BASE_GAME"}}]}}]}}}]}
            """);
        var vm = SampleData.Empty(TempFolder, network: new OfflineNetwork());

        await vm.InitializeAsync();

        Assert.Contains(vm.History.Games, r => r.Name == "Space Heroes™" && r.IsEa);
        // A history: its line in the sidebar; no token: not in the general sync.
        Assert.True(vm.Accounts.EaSync.ShowSync);
        Assert.False(vm.Accounts.EaSync.IsConnected);
    }
}
