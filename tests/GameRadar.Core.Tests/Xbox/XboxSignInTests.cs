using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using GameRadar.Core.Resources;
using GameRadar.Core.Settings;
using GameRadar.Core.Xbox;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Xbox;

public class XboxSignInTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeXboxHandler _handler = new();
    private readonly HttpClient _http;

    public XboxSignInTests() => _http = new HttpClient(_handler);

    public void Dispose()
    {
        _http.Dispose();
        _folder.Dispose();
    }

    private XboxAccount Account() => new(new XboxLiveAuth(_http, "test-client"), new SecretStore(_folder.Path));

    // ---------- PKCE ----------

    [Fact]
    public void Pkce_MatchesTheRfcExample()
    {
        // Test vector from RFC 7636, appendix B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.ChallengeOf("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Pkce_VerifiersAreRandomAndUrlSafe()
    {
        string a = Pkce.CreateVerifier(), b = Pkce.CreateVerifier();

        Assert.NotEqual(a, b);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
    }

    // ---------- Token chain ----------

    [Fact]
    public async Task GetXboxCredentials_ChainsUserTokenThenXsts()
    {
        var credentials = await new XboxLiveAuth(_http, "test-client").GetXboxCredentialsAsync("msa-token", default);

        Assert.Equal("XBL3.0 x=hash;xsts-token", credentials.AuthorizationHeader);
        Assert.Equal("2535", credentials.Xuid);
        Assert.Equal("Player One", credentials.Gamertag);
        // Exact casing matters to Xbox Live, and "d=" marks an Entra app token.
        Assert.Equal("d=msa-token", _handler.UserTokenBody!["Properties"]!["RpsTicket"]!.GetValue<string>());
        Assert.Equal("RETAIL", _handler.XstsBody!["Properties"]!["SandboxId"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetXboxCredentials_ExplainsAccountWithoutXboxProfile()
    {
        _handler.XstsRefusal = 2148916233;

        var error = await Assert.ThrowsAsync<XboxLiveException>(
            () => new XboxLiveAuth(_http, "test-client").GetXboxCredentialsAsync("msa-token", default));

        Assert.Contains("profil Xbox", error.Message);
    }

    // ---------- Account: saved session ----------

    [Fact]
    public async Task GetCredentials_WithoutSessionAsksToSignIn()
    {
        var error = await Assert.ThrowsAsync<XboxLiveException>(() => Account().GetCredentialsAsync(default));

        Assert.True(error.RequiresSignIn);
        Assert.Equal(0, _handler.Requests);
    }

    [Fact]
    public async Task GetCredentials_RenewsFromSavedSessionThenReusesThem()
    {
        new SecretStore(_folder.Path).Save(Secret.XboxAccount, new SavedXboxAccount("refresh-0", "Old tag"), XboxJsonContext.Default.SavedXboxAccount);
        var account = Account();

        var first = await account.GetCredentialsAsync(default);
        var second = await account.GetCredentialsAsync(default);

        // 3 requests (refresh, user token, XSTS), then served from memory.
        Assert.Equal(3, _handler.Requests);
        Assert.Same(first, second);
        Assert.Equal("refresh-0", _handler.TokenForms[0]["refresh_token"]);

        // The rotated refresh token and the current gamertag were saved.
        var reloaded = Account();
        Assert.Equal("Player One", reloaded.Gamertag);
        Assert.Equal("refresh-1", new SecretStore(_folder.Path).Load(Secret.XboxAccount, XboxJsonContext.Default.SavedXboxAccount)!.RefreshToken);
    }

    [Fact]
    public async Task GetCredentials_DropsRevokedSession()
    {
        new SecretStore(_folder.Path).Save(Secret.XboxAccount, new SavedXboxAccount("revoked", "Player One"), XboxJsonContext.Default.SavedXboxAccount);
        var account = Account();

        var error = await Assert.ThrowsAsync<XboxLiveException>(() => account.GetCredentialsAsync(default));

        Assert.True(error.RequiresSignIn);
        Assert.False(account.IsSignedIn);
        Assert.Null(new SecretStore(_folder.Path).Load(Secret.XboxAccount, XboxJsonContext.Default.SavedXboxAccount));
    }

    [Fact]
    public async Task GetCredentials_ExplainsThatMicrosoftDisabledTheApp()
    {
        // Microsoft switched GameRadar's app off: nothing the user can do.
        new SecretStore(_folder.Path).Save(Secret.XboxAccount, new SavedXboxAccount("app-disabled", "Player One"), XboxJsonContext.Default.SavedXboxAccount);
        var account = Account();

        var error = await Assert.ThrowsAsync<XboxLiveException>(() => account.GetCredentialsAsync(default));

        Assert.Equal(CoreStrings.Xbox_AppBlocked, error.Message);
        // Signing in again would not help; the session stays, in case access comes back.
        Assert.False(error.RequiresSignIn);
        Assert.True(account.IsSignedIn);
        Assert.NotNull(new SecretStore(_folder.Path).Load(Secret.XboxAccount, XboxJsonContext.Default.SavedXboxAccount));
    }

    [Fact]
    public async Task GetCredentials_DropsTheSessionWhenMicrosoftAsksForConsentAgain()
    {
        // A new consent or a second factor: only a sign-in in the browser can fix it.
        new SecretStore(_folder.Path).Save(Secret.XboxAccount, new SavedXboxAccount("needs-consent", "Player One"), XboxJsonContext.Default.SavedXboxAccount);
        var account = Account();

        var error = await Assert.ThrowsAsync<XboxLiveException>(() => account.GetCredentialsAsync(default));

        Assert.True(error.RequiresSignIn);
        Assert.False(account.IsSignedIn);
    }

    [Fact]
    public async Task GetCredentials_KeepsTheRenewedRefreshTokenWhenXboxLiveFailsAfter()
    {
        new SecretStore(_folder.Path).Save(Secret.XboxAccount, new SavedXboxAccount("refresh-0", "Player One"), XboxJsonContext.Default.SavedXboxAccount);
        _handler.XstsRefusal = 2148916233;

        await Assert.ThrowsAsync<XboxLiveException>(() => Account().GetCredentialsAsync(default));

        // Microsoft rotated it: the old one may be dead already.
        Assert.Equal("refresh-1", new SecretStore(_folder.Path).Load(Secret.XboxAccount, XboxJsonContext.Default.SavedXboxAccount)!.RefreshToken);
    }

    [Fact]
    public async Task SignOut_DuringARenewal_StaysSignedOut()
    {
        new SecretStore(_folder.Path).Save(Secret.XboxAccount, new SavedXboxAccount("refresh-0", "Player One"), XboxJsonContext.Default.SavedXboxAccount);
        var account = Account();
        _handler.HoldXsts = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // A sync renews the session; the user signs out meanwhile.
        var renewal = account.GetCredentialsAsync(default);
        await _handler.XstsReached.Task;
        account.SignOut();
        _handler.HoldXsts.SetResult();

        var error = await Assert.ThrowsAsync<XboxLiveException>(() => renewal);
        Assert.True(error.RequiresSignIn);
        Assert.False(account.IsSignedIn);
        Assert.Null(new SecretStore(_folder.Path).Load(Secret.XboxAccount, XboxJsonContext.Default.SavedXboxAccount));
    }

    [Fact]
    public async Task FetchHistory_RetriesOnceWithNewCredentialsWhenRefused()
    {
        // Credentials revoked early (password changed elsewhere): renewed and
        // retried once, the user does not have to click again.
        new SecretStore(_folder.Path).Save(Secret.XboxAccount, new SavedXboxAccount("refresh-0", "Player One"), XboxJsonContext.Default.SavedXboxAccount);
        var client = new XboxLiveClient(_http, Account());
        _handler.TitlehubRefusals = 1;

        string json = await client.FetchHistoryJsonAsync();

        Assert.Contains("titles", json);
        Assert.Equal(2, _handler.TitlehubCalls);

        // Refused again after the renewal: explained, not retried forever.
        _handler.TitlehubRefusals = 2;
        await Assert.ThrowsAsync<XboxLiveException>(() => client.FetchHistoryJsonAsync());
    }

    [Fact]
    public void SecretStore_EncryptsTheSavedSession()
    {
        var store = new SecretStore(_folder.Path);

        store.Save(Secret.XboxAccount, new SavedXboxAccount("very-secret-refresh-token", "Player One"), XboxJsonContext.Default.SavedXboxAccount);

        string onDisk = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_folder.Path, "xbox-account.bin")));
        Assert.DoesNotContain("very-secret-refresh-token", onDisk);
        Assert.Equal("very-secret-refresh-token", store.Load(Secret.XboxAccount, XboxJsonContext.Default.SavedXboxAccount)!.RefreshToken);
    }

    // ---------- Browser sign-in (real local listener, fake Microsoft) ----------

    [Fact]
    public async Task SignIn_RedeemsTheCodeWithItsPkceProofAndSavesTheSession()
    {
        var account = Account();
        string? challenge = null;
        using var browser = new HttpClient();

        // Plays the browser: reads the sign-in URL, then comes back to the
        // app's local listener as Microsoft would after the user signed in.
        await account.SignInAsync(uri =>
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            challenge = query["code_challenge"];
            _ = browser.GetAsync($"{query["redirect_uri"]}?code=the-code&state={query["state"]}");
        });

        var form = _handler.TokenForms.Single();
        Assert.Equal("the-code", form["code"]);
        // Only the holder of the verifier matching the challenge gets tokens.
        Assert.Equal(challenge, Pkce.ChallengeOf(form["code_verifier"]));
        Assert.True(account.IsSignedIn);
        Assert.Equal("Player One", account.Gamertag);
        Assert.NotNull(new SecretStore(_folder.Path).Load(Secret.XboxAccount, XboxJsonContext.Default.SavedXboxAccount));
    }

    [Fact]
    public async Task SignIn_IgnoresAnAnswerWithAnotherStateAndWaitsForTheRealOne()
    {
        var account = Account();
        using var browser = new HttpClient();

        // A forged request reaches the local port first (another program, a
        // page probing localhost): ignored, the real answer still signs in.
        await account.SignInAsync(uri =>
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            _ = Task.Run(async () =>
            {
                await browser.GetAsync($"{query["redirect_uri"]}?code=forged&state=not-ours");
                await browser.GetAsync($"{query["redirect_uri"]}?code=the-code&state={query["state"]}");
            });
        });

        Assert.Equal("the-code", _handler.TokenForms.Single()["code"]);
        Assert.True(account.IsSignedIn);
    }

    [Fact]
    public async Task SignIn_ExplainsARefusedCodeWithoutTalkingOfAnExpiredSession()
    {
        using var browser = new HttpClient();

        var error = await Assert.ThrowsAsync<XboxLiveException>(() => Account().SignInAsync(uri =>
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            _ = browser.GetAsync($"{query["redirect_uri"]}?code=expired-code&state={query["state"]}");
        }));

        // A first sign-in has no session to expire.
        Assert.NotEqual(CoreStrings.Xbox_SessionExpired, error.Message);
        Assert.Contains("invalid_grant", error.Message);
    }

    [Fact]
    public async Task SignIn_WithAnotherAccountDoesNotKeepThePreviousGamertag()
    {
        new SecretStore(_folder.Path).Save(Secret.XboxAccount, new SavedXboxAccount("refresh-0", "Old tag"), XboxJsonContext.Default.SavedXboxAccount);
        var account = Account();
        _handler.OmitGamertag = true;
        using var browser = new HttpClient();

        await account.SignInAsync(uri =>
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            _ = browser.GetAsync($"{query["redirect_uri"]}?code=the-code&state={query["state"]}");
        });

        // Not "Old tag": the new account says nothing, so nothing is shown.
        Assert.Null(account.Gamertag);
    }

    [Fact]
    public async Task SignIn_ExplainsThatMicrosoftDisabledTheApp()
    {
        using var browser = new HttpClient();

        // Microsoft comes back to the app with an error instead of a code.
        var error = await Assert.ThrowsAsync<XboxLiveException>(() => Account().SignInAsync(uri =>
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            _ = browser.GetAsync($"{query["redirect_uri"]}?error=unauthorized_client&state={query["state"]}");
        }));

        Assert.Equal(CoreStrings.Xbox_AppBlocked, error.Message);
        Assert.Empty(_handler.TokenForms);
    }

    [Fact]
    public async Task SignIn_CanBeCancelledWhileWaitingForTheBrowser()
    {
        using var cts = new CancellationTokenSource();

        // The user clicks "Annuler" instead of signing in.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Account().SignInAsync(_ => cts.CancelAfter(50), cts.Token));
    }

    /// <summary>Answers like Microsoft and Xbox Live, and records what it received.</summary>
    private sealed class FakeXboxHandler : HttpMessageHandler
    {
        private int _refreshTokens;

        public int Requests { get; private set; }
        public List<Dictionary<string, string>> TokenForms { get; } = [];
        public JsonNode? UserTokenBody { get; private set; }
        public JsonNode? XstsBody { get; private set; }
        public long? XstsRefusal { get; set; }
        /// <summary>Holds the XSTS answer until set (something happens meanwhile).</summary>
        public TaskCompletionSource? HoldXsts { get; set; }
        public TaskCompletionSource XstsReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool OmitGamertag { get; set; }
        /// <summary>Titlehub answers 401 this many times first (credentials revoked early).</summary>
        public int TitlehubRefusals { get; set; }
        public int TitlehubCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);

            switch (request.RequestUri!.Host)
            {
                case "login.microsoftonline.com":
                    var form = HttpUtility.ParseQueryString(body);
                    TokenForms.Add(form.AllKeys.ToDictionary(k => k!, k => form[k]!));
                    if (form["refresh_token"] == "revoked")
                        return Json(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
                    if (form["refresh_token"] == "needs-consent")
                        return Json(HttpStatusCode.BadRequest, """{ "error": "interaction_required" }""");
                    if (form["code"] == "expired-code")
                        return Json(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");
                    if (form["refresh_token"] == "app-disabled")
                        return Json(HttpStatusCode.BadRequest, """
                            { "error": "unauthorized_client", "error_codes": [7000112],
                              "error_description": "AADSTS7000112: Application 'bd8f6bbf' is disabled." }
                            """);
                    _refreshTokens++;
                    return Json(HttpStatusCode.OK, $$"""{ "access_token": "msa-token", "refresh_token": "refresh-{{_refreshTokens}}" }""");

                case "user.auth.xboxlive.com":
                    UserTokenBody = JsonNode.Parse(body);
                    return Json(HttpStatusCode.OK, """{ "Token": "user-token", "DisplayClaims": { "xui": [ { "uhs": "hash" } ] } }""");

                case "xsts.auth.xboxlive.com":
                    XstsBody = JsonNode.Parse(body);
                    XstsReached.TrySetResult();
                    if (HoldXsts is { } hold)
                        await hold.Task;
                    if (XstsRefusal is { } xerr)
                        return Json(HttpStatusCode.Unauthorized, $$"""{ "XErr": {{xerr}} }""");
                    string gamertag = OmitGamertag ? "" : ", \"gtg\": \"Player One\"";
                    return Json(HttpStatusCode.OK, $$"""
                        { "Token": "xsts-token", "NotAfter": "2099-01-01T00:00:00Z",
                          "DisplayClaims": { "xui": [ { "uhs": "hash", "xid": "2535"{{gamertag}} } ] } }
                        """);

                case "titlehub.xboxlive.com":
                    TitlehubCalls++;
                    if (TitlehubRefusals-- > 0)
                        return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                    return Json(HttpStatusCode.OK, """{ "titles": [] }""");

                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
