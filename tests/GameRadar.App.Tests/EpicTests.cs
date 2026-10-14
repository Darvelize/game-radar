using System.Net;
using System.Text;
using Avalonia.Headless.XUnit;
using GameRadar.App.Resources;

namespace GameRadar.App.Tests;

/// <summary>
/// The Epic history in the app: a code pasted once, the session kept
/// encrypted, the played games with their badge. Epic answers what the fake
/// network says.
/// </summary>
public sealed class EpicTests : AppTest
{
    private const string Code = "0123456789abcdef0123456789abcdef";
    private const string RefreshToken = "eg1~refresh-secret";
    private static readonly string Page = $$"""{"redirectUrl":"https://localhost/launcher/authorized?code={{Code}}","authorizationCode":"{{Code}}","sid":null}""";

    /// <summary>A fake Epic: one game played (no achievements), one DLC; or the code refused.</summary>
    private sealed class FakeEpic(HttpStatusCode tokenStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            string path = uri.AbsolutePath;
            if (path.EndsWith("/oauth/token"))
                return Task.FromResult(tokenStatus == HttpStatusCode.OK
                    ? FakeStores.Json($$"""{"access_token":"eg1~access","expires_in":129600,"refresh_token":"{{RefreshToken}}","account_id":"acc"}""")
                    : new HttpResponseMessage(tokenStatus) { Content = new StringContent("""{"errorCode":"errors.com.epicgames.account.oauth.authorization_code_not_found"}""") });
            if (path.EndsWith("/library/api/public/items"))
                return Task.FromResult(FakeStores.Json("""{"records":[{"appName":"Bianca","namespace":"ns-aw2","catalogItemId":"i-aw2"},{"appName":"Dlc","namespace":"ns-aw2","catalogItemId":"i-dlc"}],"responseMetadata":{}}"""));
            if (path.Contains("/playtime/"))
                return Task.FromResult(FakeStores.Json("""[{"artifactId":"Bianca","totalTime":90000},{"artifactId":"Dlc","totalTime":90000}]"""));
            if (path.Contains("/bulk/items"))
                return Task.FromResult(FakeStores.Json(uri.Query.Contains("i-dlc")
                    ? """{"i-dlc":{"title":"Alan Wake 2: Night Springs","categories":[{"path":"addons"}]}}"""
                    : """{"i-aw2":{"title":"Alan Wake 2","categories":[{"path":"games"}]}}"""));
            if (uri.Host == "launcher.store.epicgames.com")
                return Task.FromResult(FakeStores.Json("""{"data":{"Achievement":{"productAchievementsRecordBySandbox":{"totalAchievements":null}}}}"""));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [AvaloniaFact]
    public async Task Pasting_the_Epic_page_brings_the_played_games_and_clears_the_field()
    {
        var vm = SampleData.Empty(TempFolder, network: new FakeEpic());
        vm.Accounts.EpicCodeInput = Page;

        await vm.Accounts.ConnectEpicCommand.ExecuteAsync(null);

        Assert.Equal("", vm.Accounts.EpicCodeInput);
        Assert.True(vm.Accounts.EpicSync.IsConnected);
        // The game, not its DLC.
        var row = Assert.Single(vm.History.Games, r => r.IsEpic);
        Assert.Equal("Alan Wake 2", row.Name);
        // The session is kept encrypted: never in clear in any file.
        Assert.DoesNotContain(RefreshToken, string.Concat(Directory.GetFiles(TempFolder, "*", SearchOption.AllDirectories)
            .Select(f => Encoding.UTF8.GetString(File.ReadAllBytes(f)))));
    }

    [AvaloniaFact]
    public async Task A_code_Epic_refuses_is_explained_and_cleared()
    {
        var vm = SampleData.Empty(TempFolder, network: new FakeEpic(HttpStatusCode.BadRequest));
        vm.Accounts.EpicCodeInput = Code;

        await vm.Accounts.ConnectEpicCommand.ExecuteAsync(null);

        Assert.Equal("", vm.Accounts.EpicCodeInput);
        Assert.False(vm.Accounts.EpicSync.IsConnected);
        Assert.Equal(GameRadar.Core.Resources.CoreStrings.Epic_CodeRefused, vm.Accounts.EpicSync.Status);
    }

    [AvaloniaFact]
    public async Task Signing_out_forgets_the_session()
    {
        var vm = SampleData.Empty(TempFolder, network: new FakeEpic());
        vm.Accounts.EpicCodeInput = Code;
        await vm.Accounts.ConnectEpicCommand.ExecuteAsync(null);

        vm.Accounts.DisconnectEpicCommand.Execute(null);

        Assert.False(vm.Accounts.EpicSync.IsConnected);
        Assert.Equal(Strings.Account_EpicSignedOut, vm.Accounts.EpicSync.Status);
        Assert.Empty(Directory.GetFiles(TempFolder, "epic-account.bin", SearchOption.AllDirectories));
    }
}
