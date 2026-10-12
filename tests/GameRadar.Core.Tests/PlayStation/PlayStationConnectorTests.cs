using System.Net;
using System.Text;
using System.Web;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Resources;
using GameRadar.Core.Settings;
using GameRadar.Core.Xbox;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.PlayStation;

public class PlayStationConnectorTests : IDisposable
{
    private const string Npsso = "GoodNpsso0123456789abcdefGoodNpsso0123456789abcdefGoodNpsso01234";

    private readonly TempFolder _folder = new();
    private readonly FakePlayStationHandler _handler = new();
    private readonly HttpClient _http;

    public PlayStationConnectorTests() => _http = new HttpClient(_handler);

    public void Dispose()
    {
        _http.Dispose();
        _folder.Dispose();
    }

    private PlayStationAccount Account() => new(new PlayStationAuth(_http), new SecretStore(_folder.Path));

    private async Task<PlayStationAccount> ConnectedAccountAsync()
    {
        var account = Account();
        await account.ConnectAsync(Npsso);
        _handler.Calls.Clear();
        return account;
    }

    // ---------- NPSSO ----------

    [Theory]
    [InlineData(Npsso, Npsso)]
    [InlineData("  " + Npsso + "\n", Npsso)]
    [InlineData("{\"npsso\":\"" + Npsso + "\"}", Npsso)]
    [InlineData("", null)]
    [InlineData("npsso", null)]
    [InlineData("https://ca.account.sony.com/api/v1/ssocookie", null)]
    public void ParseNpsso_AcceptsTheValueOrThePageCopiedWhole(string pasted, string? expected)
    {
        Assert.Equal(expected, PlayStationAccount.ParseNpsso(pasted));
    }

    // ---------- Session ----------

    [Fact]
    public async Task Connect_SignsInWithTheNpssoAndSavesItEncrypted()
    {
        var account = Account();

        await account.ConnectAsync(Npsso);

        Assert.Equal(["authorize", "token:authorization_code"], _handler.Calls);
        Assert.True(account.IsConnected);
        Assert.True(Account().IsConnected);
        string onDisk = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_folder.Path, "playstation-account.bin")));
        Assert.DoesNotContain(Npsso, onDisk);
        Assert.DoesNotContain("refresh-1", onDisk);
    }

    [Fact]
    public async Task Connect_ExplainsARefusedNpsso()
    {
        _handler.NpssoExpired = true;
        var account = Account();

        var error = await Assert.ThrowsAsync<PlayStationException>(() => account.ConnectAsync(Npsso));

        Assert.True(error.RequiresSignIn);
        Assert.Equal(CoreStrings.PlayStation_NpssoRefused, error.Message);
        Assert.False(account.IsConnected);
    }

    [Fact]
    public async Task Connect_ExplainsThatSonyBlockedTheApp()
    {
        // Signing in again cannot help: not a "requires sign-in" error.
        _handler.ClientBlocked = true;

        var error = await Assert.ThrowsAsync<PlayStationException>(() => Account().ConnectAsync(Npsso));

        Assert.False(error.RequiresSignIn);
        Assert.Equal(CoreStrings.PlayStation_AppBlocked, error.Message);
    }

    [Fact]
    public async Task GetAccessToken_RefreshesThenSignsInAgainWithTheNpsso()
    {
        // Tokens already expired: every call renews them.
        _handler.AccessExpiresIn = 0;
        var account = await ConnectedAccountAsync();

        await account.GetAccessTokenAsync(default);
        Assert.Equal(["token:refresh_token"], _handler.Calls);

        // The refresh token is dead (~10 days): the saved NPSSO signs in again, silently.
        _handler.RefreshRefused = true;
        _handler.Calls.Clear();
        await account.GetAccessTokenAsync(default);
        Assert.Equal(["token:refresh_token", "authorize", "token:authorization_code"], _handler.Calls);
        Assert.True(account.IsConnected);
    }

    [Fact]
    public async Task GetAccessToken_DropsTheSessionWhenTheNpssoIsDeadToo()
    {
        _handler.AccessExpiresIn = 0;
        var account = await ConnectedAccountAsync();
        _handler.RefreshRefused = true;
        _handler.NpssoExpired = true;

        var error = await Assert.ThrowsAsync<PlayStationException>(() => account.GetAccessTokenAsync(default));

        Assert.True(error.RequiresSignIn);
        Assert.False(account.IsConnected);
        Assert.False(File.Exists(Path.Combine(_folder.Path, "playstation-account.bin")));
    }

    [Fact]
    public async Task GetAccessToken_KeepsTheNpssoThroughASonyOutage()
    {
        // Sony in maintenance while the session is renewed: not a dead NPSSO.
        _handler.AccessExpiresIn = 0;
        var account = await ConnectedAccountAsync();
        _handler.RefreshRefused = true;
        _handler.AuthorizeOutage = HttpStatusCode.ServiceUnavailable;

        var error = await Assert.ThrowsAsync<PlayStationException>(() => account.GetAccessTokenAsync(default));

        Assert.False(error.RequiresSignIn);
        Assert.True(account.IsConnected);
        Assert.True(File.Exists(Path.Combine(_folder.Path, "playstation-account.bin")));
    }

    [Fact]
    public async Task GetAccessToken_KeepsTheNpssoWhenTheOneTimeCodeIsRefused()
    {
        // invalid_grant on the code (used, expired): about the code, not the NPSSO.
        _handler.AccessExpiresIn = 0;
        var account = await ConnectedAccountAsync();
        _handler.RefreshRefused = true;
        _handler.CodeRefused = true;

        var error = await Assert.ThrowsAsync<PlayStationException>(() => account.GetAccessTokenAsync(default));

        Assert.False(error.RequiresSignIn);
        Assert.True(account.IsConnected);
    }

    [Fact]
    public async Task GetAccessToken_SignsInWithTheNpssoWhenTheRefreshFailsOtherwise()
    {
        _handler.AccessExpiresIn = 0;
        var account = await ConnectedAccountAsync();
        _handler.RefreshServerError = true;

        await account.GetAccessTokenAsync(default);

        Assert.Equal(["token:refresh_token", "authorize", "token:authorization_code"], _handler.Calls);
    }

    [Fact]
    public async Task GetAccessToken_UsesTheTokensWhenSonyLeavesTheirDurationsOut()
    {
        // No expires_in: not "already expired", which would sign in again at every call.
        _handler.OmitExpiries = true;
        var account = await ConnectedAccountAsync();

        await account.GetAccessTokenAsync(default);
        await account.GetAccessTokenAsync(default);

        Assert.Empty(_handler.Calls);
    }

    [Fact]
    public async Task Disconnect_DuringARenewal_StaysDisconnected()
    {
        _handler.AccessExpiresIn = 0;
        var account = await ConnectedAccountAsync();
        _handler.TokenReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.HoldToken = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // A sync renews the token; the user disconnects meanwhile.
        var renewal = account.GetAccessTokenAsync(default);
        await _handler.TokenReached.Task;
        account.Disconnect();
        _handler.HoldToken.SetResult();

        var error = await Assert.ThrowsAsync<PlayStationException>(() => renewal);
        Assert.True(error.RequiresSignIn);
        Assert.False(account.IsConnected);
        Assert.False(File.Exists(Path.Combine(_folder.Path, "playstation-account.bin")));
    }

    [Fact]
    public async Task ForgetAccessToken_OnlyForgetsTheTokenThatWasRefused()
    {
        // Both lists are fetched together: a 401 on an old token must not
        // wipe the one the other list just renewed.
        var account = await ConnectedAccountAsync();
        string current = await account.GetAccessTokenAsync(default);

        account.ForgetAccessToken("an-older-token");

        Assert.Equal(current, await account.GetAccessTokenAsync(default));
        Assert.Empty(_handler.Calls);
    }

    // ---------- History ----------

    [Fact]
    public async Task Refresh_KeepsTheTrophySetsFetchedBeforeAFailure()
    {
        // 7 played games: 2 requests of trophy sets; Sony limits the rate at the 2nd.
        _handler.ExtraPlayedGames = 4;
        _handler.TrophySetsAnswers = 1;
        var cache = new CacheStore(_folder.Path);
        var client = new PlayStationClient(_http, await ConnectedAccountAsync());

        await Assert.ThrowsAsync<PlayStationException>(() => new PlayStationHistorySource(cache).RefreshAsync(client));

        // The first 5 are kept: the next refresh asks only for the other 2.
        string saved = cache.TryLoad("playstation-trophy-sets")!.Content;
        Assert.Contains("CUSA00001_00", saved);
        Assert.DoesNotContain("CUSA10003_00", saved);
    }

    [Fact]
    public async Task Refresh_AsksAgainForATitleTheAnswerLeftOut()
    {
        var source = new PlayStationHistorySource(new CacheStore(_folder.Path));
        var client = new PlayStationClient(_http, await ConnectedAccountAsync());
        _handler.OmittedFromTrophySets = "CUSA00002_00";
        await source.RefreshAsync(client);

        // Left out of the answer is not "no trophies": not kept, asked again.
        _handler.OmittedFromTrophySets = null;
        _handler.Calls.Clear();
        await source.RefreshAsync(client);

        Assert.Contains("trophySets:CUSA00002_00", _handler.Calls);
    }

    [Fact]
    public async Task Refresh_ReadsEveryPageWhenSonyLeavesTheTotalOut()
    {
        _handler.TrophyTitlesPageSize = 1;
        _handler.OmitTrophyTitlesTotal = true;
        var source = new PlayStationHistorySource(new CacheStore(_folder.Path));

        var history = await source.RefreshAsync(new PlayStationClient(_http, await ConnectedAccountAsync()));

        // Without a total, read until an empty page: no game is cut off.
        Assert.Contains(history.Games, g => g.PlatformId == "NPWR00003_00");
    }

    [Fact]
    public void ParseGames_TakesTheNameWhenTheLocalizedOneIsEmpty()
    {
        var games = PlayStationClient.ParseGames(PlayStationClient.ParseGamesPage("""
            { "titles": [ { "titleId": "CUSA00900_00", "localizedName": "", "name": "Bloodborne", "playDuration": "PT10H" } ] }
            """));

        Assert.Equal("Bloodborne", Assert.Single(games).Name);
    }

    [Fact]
    public async Task Refresh_LinksBothListsByIdAndKeepsGamesWithTrophiesOnly()
    {
        var source = new PlayStationHistorySource(new CacheStore(_folder.Path));

        var history = await source.RefreshAsync(new PlayStationClient(_http, await ConnectedAccountAsync()));

        // Both lists are fetched together (in either order), then the trophy sets.
        Assert.Equal(["gamelist", "trophyTitles"], _handler.Calls.Take(2).Order());
        Assert.Equal("trophySets:CUSA00001_00,CUSA00002_00,CUSA00003_00", _handler.Calls[2]);
        // English names, like every other source: games are matched by title.
        Assert.All(_handler.ApiLanguages, language => Assert.Equal("en-US", language));
        Assert.Equal(3, _handler.ApiLanguages.Count);
        Assert.Equal(4, history.Games.Count);

        // A platinum without the DLC trophies (62 %): finished, not mastered,
        // as on Xbox and Steam where mastered means 100 % DLC included.
        var heroes = history.Games.Single(g => g.PlatformId == "CUSA00001_00");
        Assert.Equal("PlayStation:CUSA00001_00", heroes.Key);
        Assert.Equal(PlayStatus.Completed, heroes.Status);
        Assert.Equal(62, heroes.ProgressPercentage);
        Assert.Equal(41 * 60 + 54, heroes.PlaytimeMinutes);

        // Linked by id despite different names ("Trophées Arena Fighter").
        var fighter = history.Games.Single(g => g.PlatformId == "CUSA00002_00");
        Assert.Equal("Arena Fighter (2015)", fighter.Name);
        Assert.True(fighter.HasAchievements);
        Assert.Equal(18, fighter.ProgressPercentage);

        // Played without any trophy: its play time alone speaks.
        var noTrophies = history.Games.Single(g => g.PlatformId == "CUSA00003_00");
        Assert.False(noTrophies.HasAchievements);
        Assert.Equal(354, noTrophies.PlaytimeMinutes);

        // A PS3 game, known by its trophies only: name cleaned, no play time.
        var classic = history.Games.Single(g => g.PlatformId == "NPWR00003_00");
        Assert.Equal("Old Classic", classic.Name);
        Assert.Null(classic.PlaytimeMinutes);
        Assert.Equal(44, classic.ProgressPercentage);
    }

    [Fact]
    public async Task Refresh_GivesThePlatformGenresInTheStoreVocabulary()
    {
        var cache = new CacheStore(_folder.Path);
        var history = await new PlayStationHistorySource(cache).RefreshAsync(new PlayStationClient(_http, await ConnectedAccountAsync()));

        // The last resort for a PlayStation exclusive: main genre first, the
        // ones saying nothing ("UNIQUE") left out, "ACTION" and "ADVENTURE" one genre.
        Assert.Equal(["Role playing", "Action & adventure"], history.PlatformGenres!["PlayStation:CUSA00001_00"]);
        // No genre given: no entry, the other sources decide.
        Assert.False(history.PlatformGenres.ContainsKey("PlayStation:CUSA00003_00"));
        // Read again from the cache, the same.
        Assert.Equal(history.PlatformGenres["PlayStation:CUSA00001_00"],
            new PlayStationHistorySource(cache).LoadCached()!.PlatformGenres!["PlayStation:CUSA00001_00"]);
    }

    [Theory]
    [InlineData("ROLE_PLAYING_GAMES", "Role playing")]
    [InlineData("HORROR", "Action & adventure")]
    [InlineData("SHOOTER", "Shooter")]
    [InlineData("MUSIC/RHYTHM", "Music")]
    [InlineData("UNIQUE", null)]
    [InlineData("SOMETHING_NEW", null)]
    public void GenreOf_TranslatesIntoTheStoreGenres(string playStationGenre, string? expected)
    {
        Assert.Equal(expected, PlayStationClient.GenreOf(playStationGenre));
    }

    [Fact]
    public async Task Refresh_AsksTrophySetsOnlyForGamesPlayedSince()
    {
        var source = new PlayStationHistorySource(new CacheStore(_folder.Path));
        var client = new PlayStationClient(_http, await ConnectedAccountAsync());
        await source.RefreshAsync(client);
        _handler.Calls.Clear();

        await source.RefreshAsync(client);
        Assert.Equal(["trophyTitles", "gamelist"], _handler.Calls);

        // Played again since: maybe its first trophy, asked again.
        _handler.NoTrophyGameLastPlayed = "2026-09-20T10:00:00Z";
        _handler.Calls.Clear();
        await source.RefreshAsync(client);
        Assert.Equal(["trophyTitles", "gamelist", "trophySets:CUSA00003_00"], _handler.Calls);
    }

    [Fact]
    public async Task Refresh_ReadsEveryPage()
    {
        _handler.TrophyTitlesPageSize = 2;
        var source = new PlayStationHistorySource(new CacheStore(_folder.Path));

        var history = await source.RefreshAsync(new PlayStationClient(_http, await ConnectedAccountAsync()));

        Assert.Equal(2, _handler.Calls.Count(c => c == "trophyTitles"));
        Assert.Contains(history.Games, g => g.PlatformId == "NPWR00003_00");
    }

    [Fact]
    public async Task Refresh_RetriesOnceWithANewTokenWhenPsnRefusesIt()
    {
        var client = new PlayStationClient(_http, await ConnectedAccountAsync());
        _handler.RefuseNextApiCall = true;

        await new PlayStationHistorySource(new CacheStore(_folder.Path)).RefreshAsync(client);

        Assert.Equal(["trophyTitles", "token:refresh_token", "trophyTitles"], _handler.Calls.Take(3));
    }

    [Fact]
    public async Task LoadCached_RebuildsTheHistoryWithoutNetwork()
    {
        var cache = new CacheStore(_folder.Path);
        await new PlayStationHistorySource(cache).RefreshAsync(new PlayStationClient(_http, await ConnectedAccountAsync()));
        _handler.Calls.Clear();

        var history = new PlayStationHistorySource(cache).LoadCached();

        Assert.NotNull(history);
        Assert.Empty(_handler.Calls);
        Assert.Equal(4, history.Games.Count);
        Assert.Equal(PlayStatus.Completed, history.Games.Single(g => g.PlatformId == "CUSA00001_00").Status);
    }

    // ---------- Parsing ----------

    // The user's choice: one meaning of "Platiné" on every platform.
    [Theory]
    [InlineData(62, 62, 100.0, true, PlayStatus.Mastered)]     // every trophy, DLC included
    [InlineData(51, 62, 62.0, true, PlayStatus.Completed)]     // platinum, DLC trophies missing
    [InlineData(40, 62, 80.0, false, PlayStatus.Played)]       // far, but no platinum
    public void StatusOf_MastersOnlyAtEveryTrophyAndFinishesAtThePlatinum(
        int earned, int defined, double progress, bool platinum, PlayStatus expected)
    {
        Assert.Equal(expected, PlayStationHistorySource.StatusOf(earned, defined, progress, platinum));
    }

    [Theory]
    [InlineData("PT159H21M30S", 159 * 60 + 21)]
    [InlineData("PT2H", 120)]
    [InlineData("PT0S", 1)]
    [InlineData(null, 1)]
    [InlineData("garbage", 1)]
    public void PlaytimeMinutesOf_ReadsIsoDurationsAndNeverSaysNeverPlayed(string? duration, int expected)
    {
        Assert.Equal(expected, PlayStationClient.PlaytimeMinutesOf(duration));
    }

    [Theory]
    [InlineData("Trophées Mortal Kombat X", "Mortal Kombat X")]
    [InlineData("Collection de trophées de J-STARS Victory VS+", "J-STARS Victory VS+")]
    [InlineData("Uncharted 4 Trophies", "Uncharted 4")]
    [InlineData("God of War", "God of War")]
    [InlineData("Trophées", "Trophées")]
    public void CleanTrophyTitleName_KeepsTheGamesName(string name, string expected)
    {
        Assert.Equal(expected, PlayStationClient.CleanTrophyTitleName(name));
    }

    // ---------- With the real HTTP stack ----------

    [Fact]
    public async Task SignIn_WorksWithTheAppsHttpHandler()
    {
        // The sign-in's own handler: the redirect to the PlayStation App's
        // scheme stays unfollowed (the app's shared handler tried to follow
        // it, and failed on its "port -1"), the NPSSO cookie set by hand is sent.
        int port = LoopbackListener.FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        string? cookieSeen = null;
        var server = Task.Run(async () =>
        {
            var authorize = await listener.GetContextAsync();
            cookieSeen = authorize.Request.Headers["Cookie"];
            authorize.Response.StatusCode = 302;
            authorize.Response.RedirectLocation = PlayStationAuth.RedirectUri + "?code=v3.CODE";
            authorize.Response.Close();

            var token = await listener.GetContextAsync();
            byte[] body = Encoding.UTF8.GetBytes("""{ "access_token": "access", "expires_in": 3599, "refresh_token": "refresh", "refresh_token_expires_in": 863999 }""");
            token.Response.ContentType = "application/json";
            await token.Response.OutputStream.WriteAsync(body);
            token.Response.Close();
        });
        using var http = new HttpClient(PlayStationAuth.CreateSignInHandler());

        var tokens = await new PlayStationAuth(http, $"http://localhost:{port}/").SignInAsync(Npsso, default);
        await server;

        Assert.Equal("access", tokens.AccessToken);
        Assert.Equal("npsso=" + Npsso, cookieSeen);
    }

    /// <summary>
    /// Answers like Sony and PSN for a tiny, hand-written history, and records the calls.
    /// </summary>
    private sealed class FakePlayStationHandler : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        /// <summary>The Accept-Language of each call to the lists (not the sign-in ones).</summary>
        public List<string> ApiLanguages { get; } = [];
        public bool NpssoExpired { get; set; }
        public bool ClientBlocked { get; set; }
        public bool RefreshRefused { get; set; }
        public bool RefuseNextApiCall { get; set; }
        public int AccessExpiresIn { get; set; } = 3600;
        public int TrophyTitlesPageSize { get; set; } = 800;
        public string NoTrophyGameLastPlayed { get; set; } = "2026-06-01T10:00:00Z";
        public HttpStatusCode? AuthorizeOutage { get; set; }
        public bool CodeRefused { get; set; }
        public bool RefreshServerError { get; set; }
        public bool OmitExpiries { get; set; }
        /// <summary>Holds the token answer until set (something happens meanwhile).</summary>
        public TaskCompletionSource? HoldToken { get; set; }
        public TaskCompletionSource TokenReached { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>Trophy-set requests answered before Sony limits the rate (429).</summary>
        public int TrophySetsAnswers { get; set; } = int.MaxValue;
        public string? OmittedFromTrophySets { get; set; }
        public int ExtraPlayedGames { get; set; }
        public bool OmitTrophyTitlesTotal { get; set; }

        private int _trophySetsCalls;

        private int _tokens;

        // One object per game: Space Heroes (PS4, platinum), Arena Fighter (PS4), Old Classic (PS3).
        private static readonly string[] TrophyTitles =
        [
            """
            { "npServiceName": "trophy", "npCommunicationId": "NPWR00001_00", "trophyTitleName": "Space Heroes", "trophyTitlePlatform": "PS4",
              "progress": 62, "earnedTrophies": { "bronze": 20, "silver": 5, "gold": 1, "platinum": 1 },
              "definedTrophies": { "bronze": 30, "silver": 10, "gold": 5, "platinum": 1 }, "lastUpdatedDateTime": "2026-08-01T20:00:00Z" }
            """,
            """
            { "npServiceName": "trophy", "npCommunicationId": "NPWR00002_00", "trophyTitleName": "Trophées Arena Fighter", "trophyTitlePlatform": "PS4",
              "progress": 18, "earnedTrophies": { "bronze": 8, "silver": 0, "gold": 0, "platinum": 0 },
              "definedTrophies": { "bronze": 40, "silver": 10, "gold": 3, "platinum": 1 }, "lastUpdatedDateTime": "2015-06-30T20:00:00Z" }
            """,
            """
            { "npServiceName": "trophy", "npCommunicationId": "NPWR00003_00", "trophyTitleName": "Collection de trophées de Old Classic", "trophyTitlePlatform": "PS3",
              "progress": 44, "earnedTrophies": { "bronze": 12, "silver": 3, "gold": 1, "platinum": 0 },
              "definedTrophies": { "bronze": 35, "silver": 10, "gold": 4, "platinum": 1 }, "lastUpdatedDateTime": "2014-10-30T20:00:00Z" }
            """
        ];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
            if (path.Contains("/trophy/") || path.Contains("/gamelist/"))
                ApiLanguages.Add(request.Headers.AcceptLanguage.ToString());

            if (path.EndsWith("/authorize"))
            {
                Calls.Add("authorize");
                if (AuthorizeOutage is { } outage)
                    return new HttpResponseMessage(outage) { Content = new StringContent("<html>maintenance</html>") };
                string? cookie = request.Headers.TryGetValues("Cookie", out var values) ? values.Single() : null;
                string answer = ClientBlocked ? "error=unauthorized_client"
                    : NpssoExpired || cookie != "npsso=" + Npsso ? "error=login_required&error_code=4165"
                    : "code=v3.CODE";
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri(PlayStationAuth.RedirectUri + "?" + answer);
                return redirect;
            }

            if (path.EndsWith("/token"))
            {
                var form = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(ct));
                Calls.Add("token:" + form["grant_type"]);
                TokenReached.TrySetResult();
                if (HoldToken is { } hold)
                    await hold.Task;
                if (form["grant_type"] == "authorization_code" && CodeRefused)
                    return Json("""{ "error": "invalid_grant" }""", HttpStatusCode.BadRequest);
                if (form["grant_type"] == "refresh_token" && RefreshServerError)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("oops") };
                if (OmitExpiries)
                    return Json($$"""{ "access_token": "access-{{++_tokens}}", "refresh_token": "refresh-1" }""");
                if (form["grant_type"] == "refresh_token" && RefreshRefused)
                    return Json("""{ "error": "invalid_grant" }""", HttpStatusCode.BadRequest);
                return Json($$"""
                    { "access_token": "access-{{++_tokens}}", "expires_in": {{AccessExpiresIn}},
                      "refresh_token": "refresh-1", "refresh_token_expires_in": 863999 }
                    """);
            }

            if (request.Headers.Authorization is not { Scheme: "Bearer" } || RefuseNextApiCall)
            {
                Calls.Add(path.EndsWith("/users/me/trophyTitles") ? "trophyTitles" : path);
                RefuseNextApiCall = false;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            if (path.EndsWith("/titles/trophyTitles"))
            {
                string ids = query["npTitleIds"]!;
                Calls.Add("trophySets:" + ids);
                if (++_trophySetsCalls > TrophySetsAnswers)
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                var titles = ids.Split(',').Where(id => id != OmittedFromTrophySets).Select(id => id switch
                {
                    "CUSA00001_00" => """{ "npTitleId": "CUSA00001_00", "trophyTitles": [ { "npCommunicationId": "NPWR00001_00" } ] }""",
                    "CUSA00002_00" => """{ "npTitleId": "CUSA00002_00", "trophyTitles": [ { "npCommunicationId": "NPWR00002_00" } ] }""",
                    _ => $$"""{ "npTitleId": "{{id}}", "trophyTitles": [] }"""
                });
                return Json($$"""{ "titles": [ {{string.Join(",", titles)}} ] }""");
            }

            if (path.EndsWith("/users/me/trophyTitles"))
            {
                Calls.Add("trophyTitles");
                int offset = int.Parse(query["offset"]!);
                var page = TrophyTitles.Skip(offset).Take(TrophyTitlesPageSize);
                string total = OmitTrophyTitlesTotal ? "" : $", \"totalItemCount\": {TrophyTitles.Length}";
                return Json($$"""{ "trophyTitles": [ {{string.Join(",", page)}} ]{{total}} }""");
            }

            if (path.EndsWith("/users/me/titles"))
            {
                Calls.Add("gamelist");
                return Json($$"""
                    { "titles": [
                        { "titleId": "CUSA00001_00", "name": "Space Heroes", "localizedName": "Space Heroes", "category": "ps4_game",
                          "playCount": 15, "playDuration": "PT41H54M12S", "lastPlayedDateTime": "2026-08-01T20:00:00Z",
                          "concept": { "genres": [ "ROLE_PLAYING_GAMES", "UNIQUE", "ACTION", "ADVENTURE" ] } },
                        { "titleId": "CUSA00002_00", "name": "Arena Fighter (2015)", "localizedName": "Arena Fighter (2015)", "category": "ps4_game",
                          "playCount": 33, "playDuration": "PT37H24M", "lastPlayedDateTime": "2015-08-15T20:00:00Z" },
                        { "titleId": "CUSA00003_00", "name": "No Trophy Game", "category": "unknown",
                          "playCount": 5, "playDuration": "PT5H54M", "lastPlayedDateTime": "{{NoTrophyGameLastPlayed}}" }
                        {{string.Concat(Enumerable.Range(0, ExtraPlayedGames).Select(i => $", {{ \"titleId\": \"CUSA1000{i}_00\", \"name\": \"Extra {i}\", \"category\": \"ps4_game\", \"playDuration\": \"PT1H\", \"lastPlayedDateTime\": \"2026-01-01T00:00:00Z\" }}"))}}
                      ], "nextOffset": 3, "totalItemCount": {{3 + ExtraPlayedGames}} }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
