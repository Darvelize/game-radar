using System.Net;
using System.Text.Json;
using GameRadar.Core.Steam;

using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Steam;

public class SteamWishlistTests : IDisposable
{
    private readonly FakeHandler _handler = new();
    private readonly HttpClient _http;

    public SteamWishlistTests() => _http = new HttpClient(_handler);

    public void Dispose() => _http.Dispose();

    // ---------- Parsing ----------

    [Fact]
    public void ParseWishlist_ReadsTheAppIds()
    {
        var ids = SteamClient.ParseWishlist("""
            { "response": { "items": [
                { "appid": 1245620, "priority": 1, "date_added": 1700000000 },
                { "appid": 367520, "priority": 2, "date_added": 1710000000 }
            ] } }
            """);

        Assert.Equal(["1245620", "367520"], ids.Order());
    }

    [Fact]
    public void ParseWishlist_ReadsAnEmptyOrHiddenListAsEmpty()
    {
        // A private list answers no "items": it cannot be told from an empty one.
        Assert.Empty(SteamClient.ParseWishlist("""{ "response": {} }"""));
        Assert.Empty(SteamClient.ParseWishlist("""{ "response": { "items": [] } }"""));
        Assert.Empty(SteamClient.ParseWishlist("{}"));
    }

    [Fact]
    public void ParseWishlist_SkipsMissingIdsAndDuplicates()
    {
        var ids = SteamClient.ParseWishlist("""
            { "response": { "items": [ { "appid": 10 }, { "appid": 10 }, { "priority": 3 }, { "appid": 0 } ] } }
            """);

        Assert.Equal(["10"], ids);
    }

    [Fact]
    public void ParseWishlist_RejectsInvalidJson()
    {
        Assert.ThrowsAny<JsonException>(() => SteamClient.ParseWishlist("<html>maintenance</html>"));
    }

    // ---------- HTTP ----------

    [Fact]
    public async Task FetchWishlistJson_ReturnsTheRawAnswerAndAsksForTheRightUser()
    {
        _handler.Body = """{ "response": { "items": [ { "appid": 10 } ] } }""";

        string json = await new SteamClient(_http, "good-key").FetchWishlistJsonAsync("76561198000000001");

        Assert.Equal(_handler.Body, json);
        Assert.Single(_handler.Urls);
        Assert.Contains("IWishlistService/GetWishlist/v1", _handler.Urls[0]);
        Assert.Contains("steamid=76561198000000001", _handler.Urls[0]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Clé API Steam refusée")]
    [InlineData(HttpStatusCode.TooManyRequests, "limite les requêtes")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task FetchWishlistJson_ExplainsAnErrorWithoutLeakingTheKey(HttpStatusCode status, string expected)
    {
        _handler.Status = status;

        var error = await Assert.ThrowsAsync<SteamException>(
            () => new SteamClient(_http, "secret-key-123").FetchWishlistJsonAsync("76561198000000001"));

        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("secret-key-123", error.Message);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(Json(Body, Status));
        }
    }
}
