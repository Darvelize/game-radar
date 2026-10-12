using System.Net;
using System.Text;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.PlayStation;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.PlayStation;

public class PsPlusCatalogSourceTests
{
    private static string Block(params (int Id, string Name)[] games) =>
        "[{ \"games\": [" + string.Join(",", games.Select(g =>
            $"{{ \"conceptId\": {g.Id}, \"name\": \"{g.Name}\", \"nameEn\": \"{g.Name}\" }}")) + "] }]";

    /// <summary>A client whose lists answer what the test says.</summary>
    private static PsPlusCatalogClient ClientWith(string games, string classics) =>
        new(new HttpClient(new FakeHandler(uri => uri.Query.Contains("plus-classics-list") ? classics
            : uri.Query.Contains("plus-games-list") ? games : "[]")));

    [Fact]
    public async Task RefreshAsync_SavesTheCatalogAndLoadCachedReadsItBack()
    {
        using var folder = new TempFolder();
        var source = new PsPlusCatalogSource(new CacheStore(folder.Path));
        Assert.Null(source.LoadCached());

        var catalog = await source.RefreshAsync(ClientWith(Block((1, "Extra Game")), Block((2, "Old Classic"))));

        Assert.Equal(2, catalog.Games.Count);
        var cached = source.LoadCached()!;
        Assert.Equal(2, cached.Games.Count);
        Assert.Equal(PsPlusTier.Premium, cached.Games.Single(g => g.Title == "Old Classic").PsPlusTiers);
    }

    [Fact]
    public async Task RefreshAsync_JoinsTheConsoleVersionsOfOneTitle()
    {
        // Two concepts for one game ("X" on PS4, "X PS4 & PS5" on PS5): one candidate.
        using var folder = new TempFolder();
        var source = new PsPlusCatalogSource(new CacheStore(folder.Path));

        var catalog = await source.RefreshAsync(
            ClientWith(Block((1, "Dead Cells"), (2, "Dead Cells PS4 & PS5"), (3, "Hades")), "[]"));

        Assert.Equal(2, catalog.Games.Count);
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheOldCatalogWhenTheNewOneIsEmpty()
    {
        using var folder = new TempFolder();
        var source = new PsPlusCatalogSource(new CacheStore(folder.Path));
        await source.RefreshAsync(ClientWith(Block((1, "Extra Game")), "[]"));

        await Assert.ThrowsAsync<PlayStationException>(() => source.RefreshAsync(ClientWith("[]", "[]")));

        Assert.Single(source.LoadCached()!.Games);
    }

    [Fact]
    public async Task RefreshAsync_RefusesACatalogThatLostItsClassics()
    {
        // The Classics list answers nothing (renamed...): saved, every Premium-only game
        // would leave PS Plus for a week.
        using var folder = new TempFolder();
        var source = new PsPlusCatalogSource(new CacheStore(folder.Path));
        await source.RefreshAsync(ClientWith(Block((1, "Extra Game")), Block((2, "Old Classic"))));

        await Assert.ThrowsAsync<PlayStationException>(
            () => source.RefreshAsync(ClientWith(Block((1, "Extra Game")), "[]")));

        Assert.Equal(2, source.LoadCached()!.Games.Count);
    }

    [Fact]
    public async Task RefreshAsync_AcceptsAFirstCatalogWithoutClassics()
    {
        using var folder = new TempFolder();
        var source = new PsPlusCatalogSource(new CacheStore(folder.Path));

        var catalog = await source.RefreshAsync(ClientWith(Block((1, "Extra Game")), "[]"));

        Assert.Single(catalog.Games);
    }

    [Fact]
    public void IsStale_AfterSevenDays()
    {
        var fetched = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var catalog = new GameCatalog([], fetched);

        Assert.False(PsPlusCatalogSource.IsStale(catalog, fetched.AddDays(6)));
        Assert.True(PsPlusCatalogSource.IsStale(catalog, fetched.AddDays(8)));
    }

    private sealed class FakeHandler(Func<Uri, string> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answer(request.RequestUri!), Encoding.UTF8, "application/json")
            });
    }
}
