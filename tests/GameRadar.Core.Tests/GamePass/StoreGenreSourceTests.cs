using System.Net;
using System.Text;
using GameRadar.Core.Cache;
using GameRadar.Core.GamePass;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.GamePass;

public class StoreGenreSourceTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly FakeStoreHandler _handler = new();
    private readonly HttpClient _http;
    private readonly GamePassClient _client;

    public StoreGenreSourceTests()
    {
        // The HttpClient is injected into the connector: giving it a fake
        // handler tests the real HTTP code path without any network.
        _http = new HttpClient(_handler);
        _client = new GamePassClient(_http);
    }

    public void Dispose()
    {
        _http.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public async Task ResolveAsync_LooksUpUnknownGamesOnceThenUsesCache()
    {
        var known = Played("111");     // the Store knows it
        var unknown = Played("222");   // the Store answers 404
        var source = new StoreGenreSource(new CacheStore(_folder.Path));
        var matcher = new CatalogMatcher([]);

        var first = await source.ResolveAsync([known, unknown], matcher, _client);

        // 2 lookups + 1 details request; only the known game gets genres.
        Assert.Equal(3, _handler.Requests);
        var resolved = Assert.Single(first);
        Assert.Equal(["Shooter", "Role playing"], resolved.Genres);
        Assert.Equal(PlayModes.CoopOnline, resolved.Modes);

        var second = await source.ResolveAsync([known, unknown], matcher, _client);

        // Both answers were cached, the "not found" one included.
        Assert.Equal(3, _handler.Requests);
        Assert.Single(second);
    }

    [Fact]
    public async Task ResolveAsync_TakesGenresFromCatalogWithoutRequest()
    {
        var played = Played("111");
        var matcher = new CatalogMatcher([Catalog("P9", xboxTitleId: "111", genres: ["Racing & flying"])]);
        var source = new StoreGenreSource(new CacheStore(_folder.Path));

        var result = await source.ResolveAsync([played], matcher, _client);

        Assert.Equal(0, _handler.Requests);
        Assert.Equal(["Racing & flying"], Assert.Single(result).Genres);
    }

    [Fact]
    public async Task ResolveAsync_SkipsGamesWithoutAchievements()
    {
        var steamGame = Played("333", hasAchievements: false);
        var source = new StoreGenreSource(new CacheStore(_folder.Path));

        var result = await source.ResolveAsync([steamGame], new CatalogMatcher([]), _client);

        Assert.Equal(0, _handler.Requests);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveAsync_RemembersATitleTheStoreRefusesAndGoesOn()
    {
        // An odd title id answered 400, at every launch: it stopped the
        // genres, so the recommendations, for good.
        var refused = Played("400");
        var known = Played("111");
        var source = new StoreGenreSource(new CacheStore(_folder.Path));

        var result = await source.ResolveAsync([refused, known], new CatalogMatcher([]), _client);

        Assert.Equal("111", Assert.Single(result).Game.PlatformId);
        int requests = _handler.Requests;
        await source.ResolveAsync([refused, known], new CatalogMatcher([]), _client);
        Assert.Equal(requests, _handler.Requests);   // remembered as "not found"
    }

    [Fact]
    public async Task ResolveAsync_GoesOnWithoutTheLookupsWhenTheStoreIsDown()
    {
        var known = Played("111");
        var source = new StoreGenreSource(new CacheStore(_folder.Path));
        _handler.ProductsDown = true;

        // No genres this time, no error: the recommendations still come.
        Assert.Empty(await source.ResolveAsync([known], new CatalogMatcher([]), _client));

        _handler.ProductsDown = false;
        Assert.Single(await source.ResolveAsync([known], new CatalogMatcher([]), _client));
    }

    [Fact]
    public async Task ResolveAsync_ACancelledRunDoesNotUndoANewerOne()
    {
        // Run A waits on the Store; run B (a newer computation) finishes and
        // saves; then A is cancelled and saves what it had: B's answers stay.
        var cache = new CacheStore(_folder.Path);
        _handler.HoldLookupOf = "222";
        using var cancel = new CancellationTokenSource();
        var runA = new StoreGenreSource(cache).ResolveAsync([Played("222")], new CatalogMatcher([]), _client, ct: cancel.Token);
        await _handler.Held.Task;

        await new StoreGenreSource(cache).ResolveAsync([Played("111")], new CatalogMatcher([]), _client);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runA);

        int requests = _handler.Requests;
        Assert.Single(await new StoreGenreSource(cache).ResolveAsync([Played("111")], new CatalogMatcher([]), _client));
        Assert.Equal(requests, _handler.Requests);
    }

    /// <summary>Answers like the Store for a tiny fixed catalog, and counts requests.</summary>
    private sealed class FakeStoreHandler : HttpMessageHandler
    {
        private int _requests;
        public int Requests => _requests;
        public bool ProductsDown { get; set; }
        /// <summary>A title id whose lookup waits until the request is cancelled.</summary>
        public string? HoldLookupOf { get; set; }
        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _requests);
            string url = request.RequestUri!.ToString();
            if (HoldLookupOf is { } held && url.Contains($"/lookup?value={held}"))
            {
                Held.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            if (ProductsDown && url.Contains("bigIds="))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            var response = url switch
            {
                _ when url.Contains("/lookup?value=400") => new HttpResponseMessage(HttpStatusCode.BadRequest),
                _ when url.Contains("/lookup?value=111") => Json("""{ "Products": [ { "ProductId": "P1" } ] }"""),
                _ when url.Contains("/lookup?") => new HttpResponseMessage(HttpStatusCode.NotFound),
                _ when url.Contains("bigIds=P1") => Json("""
                    { "Products": [ {
                        "ProductId": "P1",
                        "LocalizedProperties": [ { "ProductTitle": "Known" } ],
                        "Properties": {
                            "Category": "Shooter", "Categories": [ "Role playing" ],
                            "Attributes": [ { "Name": "XblOnlineCoop" } ]
                        }
                    } ] }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            };
            return response;
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
