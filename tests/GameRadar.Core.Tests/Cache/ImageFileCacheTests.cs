using System.Net;
using System.Net.Http.Headers;
using GameRadar.Core.Cache;
using static GameRadar.Core.Tests.TestData;

namespace GameRadar.Core.Tests.Cache;

public class ImageFileCacheTests
{
    private static readonly byte[] PictureBytes = [0xFF, 0xD8, 0xFF, 0x01, 0x02];

    [Fact]
    public async Task GetAsync_DownloadsOnceThenServesFromDisk()
    {
        using var folder = new TempFolder();
        var handler = new FakeImageHandler();
        var cache = new ImageFileCache(new HttpClient(handler), folder.Path);

        string? first = await cache.GetAsync("https://img/a");
        string? second = await cache.GetAsync("https://img/a");

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(PictureBytes, File.ReadAllBytes(first));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task GetAsync_SameUrlAskedTwiceAtOnce_DownloadsOnlyOnce()
    {
        using var folder = new TempFolder();
        var handler = new FakeImageHandler { Delay = TimeSpan.FromMilliseconds(100) };
        var cache = new ImageFileCache(new HttpClient(handler), folder.Path);

        // Two cards of the same game asking at the same time.
        var results = await Task.WhenAll(cache.GetAsync("https://img/a"), cache.GetAsync("https://img/a"));

        Assert.All(results, Assert.NotNull);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task GetAsync_DownloadEndingAfterEraseAll_WritesNothing()
    {
        using var folder = new TempFolder();
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ImageFileCache(new HttpClient(new FakeImageHandler { Hold = answer.Task }), folder.Path);

        // "Effacer toutes mes données" while a card picture is on its way: it arrives after.
        var download = cache.GetAsync("https://img/a");
        cache.EraseAll();
        answer.SetResult();

        Assert.Null(await download);
        Assert.False(Directory.Exists(folder.Path) && Directory.EnumerateFileSystemEntries(folder.Path).Any());
    }

    [Fact]
    public async Task GetAsync_APictureThatStopsArrivingGivesUpAndFreesItsSlot()
    {
        using var folder = new TempFolder();
        // Headers at once, then the body never comes: HttpClient.Timeout only
        // covers the headers (ResponseHeadersRead).
        var cache = new ImageFileCache(new HttpClient(new StalledBodyHandler()), folder.Path, downloadTimeout: TimeSpan.FromMilliseconds(200));

        var pictures = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => cache.GetAsync($"https://img/{i}")))
            .WaitAsync(TimeSpan.FromSeconds(10));

        // 5 stalled pictures for 4 slots: the 5th was not stuck behind them.
        Assert.All(pictures, Assert.Null);
    }

    [Fact]
    public async Task GetAsync_AnAddressItCannotUseGivesNull()
    {
        using var folder = new TempFolder();
        var cache = new ImageFileCache(new HttpClient(new FakeImageHandler()), folder.Path);

        // Relative: HttpClient (no BaseAddress) throws InvalidOperationException.
        Assert.Null(await cache.GetAsync("images/cover.jpg"));
    }

    [Theory]
    [InlineData("https://img/missing")]   // 404
    [InlineData("https://img/not-image")] // HTML error page with a 200
    public async Task GetAsync_Failure_ReturnsNullAndWritesNothing(string url)
    {
        using var folder = new TempFolder();
        var cache = new ImageFileCache(new HttpClient(new FakeImageHandler()), folder.Path);

        Assert.Null(await cache.GetAsync(url));
        Assert.False(Directory.Exists(folder.Path) && Directory.EnumerateFiles(folder.Path).Any());
    }

    private sealed class StalledBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new StreamContent(new NeverEndingStream());
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    [Fact]
    public async Task RemoveUnused_DeletesPicturesNotShownForAMonth_KeepsTheOthers()
    {
        using var folder = new TempFolder();
        var cache = new ImageFileCache(new HttpClient(new FakeImageHandler()), folder.Path);
        string shownLately = (await cache.GetAsync("https://img/recent"))!;
        string shownAgain = (await cache.GetAsync("https://img/shown-again"))!;
        string forgotten = (await cache.GetAsync("https://img/forgotten"))!;
        string crashedDownload = Path.Combine(folder.Path, "x.img.abc.tmp");
        string recentDownload = Path.Combine(folder.Path, "y.img.abc.tmp");
        File.WriteAllBytes(crashedDownload, PictureBytes);
        File.WriteAllBytes(recentDownload, PictureBytes);
        var longAgo = DateTime.UtcNow - ImageFileCache.UnusedLifetime - TimeSpan.FromDays(1);
        foreach (string old in new[] { shownAgain, forgotten, crashedDownload })
            File.SetLastWriteTimeUtc(old, longAgo);

        // Shown on a card today: marked as used, so it stays.
        ImageFileCache.MarkShown(shownAgain);
        cache.RemoveUnused();

        Assert.True(File.Exists(shownLately));
        Assert.True(File.Exists(shownAgain));
        Assert.True(File.Exists(recentDownload));
        Assert.False(File.Exists(forgotten));
        Assert.False(File.Exists(crashedDownload));
    }

    [Fact]
    public async Task RemoveUnused_PlayerAwayForTwoMonths_KeepsEveryPicture()
    {
        using var folder = new TempFolder();
        var cache = new ImageFileCache(new HttpClient(new FakeImageHandler()), folder.Path);
        string a = (await cache.GetAsync("https://img/a"))!;
        string b = (await cache.GetAsync("https://img/b"))!;
        // Both shown on the last day the app was opened, two months ago.
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow - TimeSpan.FromDays(60));
        File.SetLastWriteTimeUtc(b, DateTime.UtcNow - TimeSpan.FromDays(61));

        cache.RemoveUnused();

        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task MarkShown_AtMostOnceADay()
    {
        using var folder = new TempFolder();
        var cache = new ImageFileCache(new HttpClient(new FakeImageHandler()), folder.Path);
        string path = (await cache.GetAsync("https://img/a"))!;
        var halfADayAgo = DateTime.UtcNow - TimeSpan.FromHours(12);
        File.SetLastWriteTimeUtc(path, halfADayAgo);

        ImageFileCache.MarkShown(path);

        Assert.Equal(halfADayAgo, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>A body whose bytes never come (until the read is cancelled).</summary>
    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeImageHandler : HttpMessageHandler
    {
        private int _requests;
        public int Requests => _requests;
        public TimeSpan Delay { get; init; }
        /// <summary>Set: the answer waits for it (a download still on its way while the test acts).</summary>
        public Task? Hold { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Interlocked: parallel downloads may count at the same time.
            Interlocked.Increment(ref _requests);
            await Task.Delay(Delay, ct);
            if (Hold is not null)
                await Hold.WaitAsync(ct);

            string url = request.RequestUri!.ToString();
            if (url.EndsWith("/missing"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var content = url.EndsWith("/not-image")
                ? new ByteArrayContent("<html>error</html>"u8.ToArray())
                : new ByteArrayContent(PictureBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(url.EndsWith("/not-image") ? "text/html" : "image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
