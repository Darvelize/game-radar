using System.Net;
using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Steam;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Steam;

public class SteamWishlistSourceTests : IDisposable
{
    private const string SteamId = "76561198000000001";

    private readonly TempFolder _folder = new();
    private readonly FakeSteam _steam = new();
    private readonly HttpClient _http;
    private readonly CacheStore _cache;

    public SteamWishlistSourceTests()
    {
        _http = new HttpClient(_steam);
        _cache = new CacheStore(_folder.Path);
    }

    public void Dispose()
    {
        _http.Dispose();
        _folder.Dispose();
    }

    private SteamClient Client => new(_http, "good-key");

    [Fact]
    public void LoadCached_IsEmptyBeforeAnySync()
    {
        Assert.Empty(new SteamWishlistSource(_cache).LoadCached());
    }

    [Fact]
    public async Task Refresh_SavesTheRawAnswerAndLoadCachedReadsIt()
    {
        var source = new SteamWishlistSource(_cache);

        var fetched = await source.RefreshAsync(Client, SteamId);

        Assert.Equal(["10", "20"], fetched.Order());
        Assert.Equal(["10", "20"], new SteamWishlistSource(_cache).LoadCached().Order());
        // The raw answer is what is cached: a better parsing later applies to it too.
        Assert.Equal(_steam.Wishlist, _cache.TryLoad("steam-wishlist")?.Content);
    }

    [Fact]
    public async Task Refresh_KeepsTheValidCacheWhenTheAnswerIsUnexpected()
    {
        var source = new SteamWishlistSource(_cache);
        await source.RefreshAsync(Client, SteamId);
        _steam.Wishlist = "<html>maintenance</html>";

        await Assert.ThrowsAnyAsync<JsonException>(() => source.RefreshAsync(Client, SteamId));

        Assert.Equal(["10", "20"], source.LoadCached().Order());
    }

    [Fact]
    public void LoadCached_ReadsACorruptedFileAsEmpty()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(Path.Combine(_folder.Path, "steam-wishlist.json"), "{ not json");

        Assert.Empty(new SteamWishlistSource(_cache).LoadCached());
    }

    [Fact]
    public async Task TheSteamSync_AlsoFetchesTheWishlist()
    {
        var history = await new SteamHistorySource(_cache).RefreshAsync(Client, SteamId);

        Assert.Single(history.Games);
        Assert.Equal(["10", "20"], new SteamWishlistSource(_cache).LoadCached().Order());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TheSteamSync_SucceedsEvenWhenTheWishlistFails(HttpStatusCode wishlistStatus)
    {
        _steam.WishlistStatus = wishlistStatus;

        var history = await new SteamHistorySource(_cache).RefreshAsync(Client, SteamId);

        // The library is what the sync is for: it is saved, the wishlist just waits for the next sync.
        Assert.Single(history.Games);
        Assert.NotNull(new SteamHistorySource(_cache).LoadCached());
        Assert.Empty(new SteamWishlistSource(_cache).LoadCached());
    }

    [Fact]
    public async Task TheSteamSync_KeepsTheOldWishlistWhenTheNewOneFails()
    {
        await new SteamHistorySource(_cache).RefreshAsync(Client, SteamId);
        _steam.WishlistStatus = HttpStatusCode.InternalServerError;

        await new SteamHistorySource(_cache).RefreshAsync(Client, SteamId);

        Assert.Equal(["10", "20"], new SteamWishlistSource(_cache).LoadCached().Order());
    }

    private sealed class FakeSteam : HttpMessageHandler
    {
        public string Wishlist { get; set; } = """{ "response": { "items": [ { "appid": 10 }, { "appid": 20 } ] } }""";
        public HttpStatusCode WishlistStatus { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.Contains("GetWishlist"))
                return Task.FromResult(Json(WishlistStatus, Wishlist));
            if (path.Contains("GetOwnedGames"))
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    { "response": { "game_count": 1, "games": [ { "appid": 30, "name": "Owned", "playtime_forever": 0, "rtime_last_played": 0 } ] } }
                    """));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
