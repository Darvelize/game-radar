using System.Net;
using System.Text;
using GameRadar.Core.Epic;
using GameRadar.Core.Resources;
using GameRadar.Core.Settings;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Epic;

public class EpicAccountTests : IDisposable
{
    private const string Code = "0123456789abcdef0123456789abcdef";
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    /// <summary>
    /// A fake Epic: the token endpoint answers the next of <see cref="Tokens"/>
    /// (or <see cref="TokenStatus"/>), the other services answer
    /// <see cref="DataStatus"/> then 200. Records what each request carried.
    /// </summary>
    private sealed class FakeEpic : HttpMessageHandler
    {
        private int _issued;
        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public string TokenErrorCode { get; set; } = "errors.com.epicgames.account.oauth.invalid_grant";
        public Queue<HttpStatusCode> DataStatus { get; } = new();
        public TaskCompletionSource? HoldTokens { get; set; }
        public List<(string Url, string? Auth, string Agent, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), request.Headers.UserAgent.ToString(), body));
            if (request.RequestUri.AbsolutePath.EndsWith("/oauth/token"))
            {
                if (HoldTokens is { } hold)
                    await hold.Task;
                if (TokenStatus != HttpStatusCode.OK)
                    return new HttpResponseMessage(TokenStatus) { Content = new StringContent($$"""{"errorCode":"{{TokenErrorCode}}"}""") };
                int n = ++_issued;
                return Json($$"""{"access_token":"eg1~access{{n}}","expires_in":129600,"refresh_token":"eg1~refresh{{n}}","account_id":"account-1"}""");
            }
            if (DataStatus.TryDequeue(out var status))
                return new HttpResponseMessage(status) { Content = new StringContent("") };
            return Json(request.RequestUri.AbsolutePath.Contains("/playtime/") ? "[]" : """{"records":[],"responseMetadata":{}}""");
        }
    }

    private SecretStore Store() => new(_folder.Path);

    private string? SavedRefreshToken() => Store().Load(Secret.EpicAccount, EpicJsonContext.Default.SavedEpicAccount)?.RefreshToken;

    [Fact]
    public async Task ConnectAsync_ExchangesTheCodeAsTheLauncherAndKeepsTheSession()
    {
        var epic = new FakeEpic();
        var account = new EpicAccount(new HttpClient(epic), Store());

        await account.ConnectAsync($$"""{"authorizationCode":"{{Code}}"}""");

        var (url, auth, agent, body) = Assert.Single(epic.Requests);
        Assert.Equal("https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token", url);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(EpicClient.ClientId + ":" + EpicClient.ClientSecret)), auth);
        Assert.StartsWith("UELauncher/", agent);
        Assert.Equal($"grant_type=authorization_code&code={Code}&token_type=eg1", body);
        Assert.True(account.IsConnected);
        Assert.Equal("eg1~refresh1", SavedRefreshToken());
        // Read back at the next launch.
        Assert.True(new EpicAccount(new HttpClient(epic), Store()).IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_ACodeEpicRefuses_SaysToPasteAFreshOne()
    {
        var epic = new FakeEpic { TokenStatus = HttpStatusCode.BadRequest };
        var account = new EpicAccount(new HttpClient(epic), Store());

        var ex = await Assert.ThrowsAsync<EpicException>(() => account.ConnectAsync(Code));

        Assert.Equal(CoreStrings.Epic_CodeRefused, ex.Message);
        Assert.False(account.IsConnected);
        Assert.Null(SavedRefreshToken());
    }

    [Fact]
    public async Task ARenewal_RewritesTheKeptRefreshToken()
    {
        var epic = new FakeEpic();
        await new EpicAccount(new HttpClient(epic), Store()).ConnectAsync(Code);

        // A new launch: no access token in memory, renewed with the kept refresh token.
        var account = new EpicAccount(new HttpClient(epic), Store());
        string token = await account.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("eg1~access2", token);
        Assert.Equal("grant_type=refresh_token&refresh_token=eg1~refresh1&token_type=eg1", epic.Requests[^1].Body);
        // Epic replaced it: the old one is no longer valid, the new one is kept.
        Assert.Equal("eg1~refresh2", SavedRefreshToken());
        // Then from memory, no request.
        Assert.Equal("eg1~access2", await account.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(2, epic.Requests.Count);
    }

    [Fact]
    public async Task ARefusedRenewal_Disconnects()
    {
        var epic = new FakeEpic();
        await new EpicAccount(new HttpClient(epic), Store()).ConnectAsync(Code);
        epic.TokenStatus = HttpStatusCode.BadRequest;
        var account = new EpicAccount(new HttpClient(epic), Store());

        var ex = await Assert.ThrowsAsync<EpicException>(() => account.GetAccessTokenAsync(CancellationToken.None));

        Assert.True(ex.IsTokenRefused);
        Assert.Equal(CoreStrings.Epic_SessionExpired, ex.Message);
        Assert.False(account.IsConnected);
        Assert.Null(SavedRefreshToken());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "errors.com.epicgames.common.forbidden")]                       // a firewall
    [InlineData(HttpStatusCode.BadRequest, "errors.com.epicgames.oauth.corrective_action_required")]      // terms to accept
    [InlineData(HttpStatusCode.Unauthorized, "errors.com.epicgames.account.invalid_client_credentials")]  // the launcher's client changed
    public async Task ARenewalRefusedForAnotherReason_KeepsTheSession(HttpStatusCode status, string errorCode)
    {
        var epic = new FakeEpic();
        await new EpicAccount(new HttpClient(epic), Store()).ConnectAsync(Code);
        epic.TokenStatus = status;
        epic.TokenErrorCode = errorCode;
        var account = new EpicAccount(new HttpClient(epic), Store());

        var ex = await Assert.ThrowsAsync<EpicException>(() => account.GetAccessTokenAsync(CancellationToken.None));

        Assert.False(ex.IsTokenRefused);
        Assert.True(account.IsConnected);
        Assert.Equal("eg1~refresh1", SavedRefreshToken());
    }

    [Fact]
    public async Task AnAccessTokenRefusedTwice_Disconnects()
    {
        var epic = new FakeEpic();
        var account = new EpicAccount(new HttpClient(epic), Store());
        await account.ConnectAsync(Code);
        epic.DataStatus.Enqueue(HttpStatusCode.Unauthorized);
        epic.DataStatus.Enqueue(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<EpicException>(() => new EpicClient(new HttpClient(epic), account).FetchPlaytimeJsonAsync());

        Assert.True(ex.IsTokenRefused);
        Assert.False(account.IsConnected);
        Assert.Null(SavedRefreshToken());
    }

    [Fact]
    public async Task ADisconnectDuringARenewal_SavesNothing()
    {
        var epic = new FakeEpic();
        await new EpicAccount(new HttpClient(epic), Store()).ConnectAsync(Code);
        epic.HoldTokens = new TaskCompletionSource();
        var account = new EpicAccount(new HttpClient(epic), Store());

        var renewing = account.GetAccessTokenAsync(CancellationToken.None);
        account.Disconnect();
        epic.HoldTokens.SetResult();

        await Assert.ThrowsAsync<EpicException>(() => renewing);
        Assert.Null(SavedRefreshToken());
    }

    [Fact]
    public async Task TheLibrary_IsReadWithTheAccessTokenAsTheLauncher()
    {
        var epic = new FakeEpic();
        var account = new EpicAccount(new HttpClient(epic), Store());
        await account.ConnectAsync(Code);

        await new EpicClient(new HttpClient(epic), account).FetchLibraryPageJsonAsync("next page");

        var (url, auth, agent, _) = epic.Requests[^1];
        Assert.Equal("https://library-service.live.use1a.on.epicgames.com/library/api/public/items?includeMetadata=true&cursor=next%20page", url);
        Assert.Equal("Bearer eg1~access1", auth);
        Assert.StartsWith("UELauncher/", agent);
    }

    [Fact]
    public async Task TheAchievements_AreAskedToTheStoreAsTheLauncher()
    {
        var epic = new FakeEpic();
        var account = new EpicAccount(new HttpClient(epic), Store());
        await account.ConnectAsync(Code);
        epic.DataStatus.Enqueue(HttpStatusCode.OK);

        // An empty 200 is not a GraphQL answer: a format error, but the request is what is checked.
        await Assert.ThrowsAsync<EpicException>(() => new EpicClient(new HttpClient(epic), account).FetchAchievementsUnlockedAsync("ns-1"));

        var (url, _, agent, body) = epic.Requests[^1];
        Assert.Equal("https://launcher.store.epicgames.com/graphql", url);
        Assert.StartsWith("EpicGamesLauncher/", agent);
        Assert.Contains("\"epicAccountId\":\"account-1\"", body);
        Assert.Contains("\"sandboxId\":\"ns-1\"", body);
    }

    [Fact]
    public async Task AnAccessTokenRefusedOnce_IsRenewedAndTheRequestSentAgain()
    {
        var epic = new FakeEpic();
        var account = new EpicAccount(new HttpClient(epic), Store());
        await account.ConnectAsync(Code);
        epic.DataStatus.Enqueue(HttpStatusCode.Unauthorized);

        await new EpicClient(new HttpClient(epic), account).FetchPlaytimeJsonAsync();

        Assert.Equal("Bearer eg1~access2", epic.Requests[^1].Auth);
        Assert.Equal("eg1~refresh2", SavedRefreshToken());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, false, true)]
    [InlineData(HttpStatusCode.Forbidden, false, false)] // one service refusing, not the session
    [InlineData(HttpStatusCode.InternalServerError, false, false)]
    public async Task TheServiceStatuses_AreExplained(HttpStatusCode status, bool refused, bool rateLimited)
    {
        var epic = new FakeEpic();
        var account = new EpicAccount(new HttpClient(epic), Store());
        await account.ConnectAsync(Code);
        epic.DataStatus.Enqueue(status);

        var ex = await Assert.ThrowsAsync<EpicException>(() => new EpicClient(new HttpClient(epic), account).FetchPlaytimeJsonAsync());

        Assert.Equal((refused, rateLimited), (ex.IsTokenRefused, ex.IsRateLimited));
    }
}
