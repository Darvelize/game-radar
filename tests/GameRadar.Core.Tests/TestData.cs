using System.Net;
using System.Text;
using GameRadar.Core.Models;

namespace GameRadar.Core.Tests;

/// <summary>
/// Builders with sensible defaults: each test only states what matters to it.
/// Hand-written data only: real API answers hold personal play history and
/// must not end up in the repository.
/// </summary>
internal static class TestData
{
    /// <summary>Fixed "now": tests must not depend on the day they run.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    public static PlayedGame Played(
        string id,
        double progress = 0,
        PlayStatus status = PlayStatus.Played,
        DateTimeOffset? lastPlayed = null,
        string? name = null,
        bool hasAchievements = true,
        string platform = "Xbox",
        string? packageFamilyName = null) =>
        new(
            Platform: platform,
            PlatformId: id,
            Name: name ?? $"Played {id}",
            HasAchievements: hasAchievements,
            UnlockedAchievements: 0,
            TotalAchievements: null,
            ProgressPercentage: progress,
            // Default: played long ago, so a low progress counts as dropped.
            LastPlayed: lastPlayed ?? Now.AddDays(-90),
            Status: status,
            PackageFamilyName: packageFamilyName)
        {
            // As the Steam connector gives it: a Steam game's id is its app id.
            SteamAppId = platform == "Steam" ? id : null
        };

    /// <summary>A Steam game owned but never launched (0 minutes, no achievements).</summary>
    public static PlayedGame NeverLaunched(string appId, string name) =>
        Played(appId, platform: "Steam", name: name, hasAchievements: false) with { PlaytimeMinutes = 0 };

    public static CatalogGame Catalog(
        string productId,
        string? title = null,
        string[]? genres = null,
        string? xboxTitleId = null,
        string? packageFamilyName = null,
        double? rating = null,
        int ratingCount = 0,
        PlayModes modes = PlayModes.None,
        string? steamAppId = null,
        bool inGamePass = true) =>
        new(
            ProductId: productId,
            Title: title ?? $"Catalog {productId}",
            XboxTitleId: xboxTitleId,
            PackageFamilyName: packageFamilyName,
            Genres: genres ?? ["Action & adventure"],
            OnPc: inGamePass,
            OnConsole: inGamePass,
            LeavingSoon: false,
            AverageRating: rating,
            RatingCount: ratingCount,
            Modes: modes,
            SteamAppId: steamAppId);

    public static PlayedGameWithGenres WithGenres(PlayedGame game, params string[] genres) => new(game, genres);

    /// <summary>A fake server's JSON answer.</summary>
    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A fake server answering each address with the JSON <paramref name="answer"/> gives.</summary>
    public sealed class JsonHandler(Func<Uri, string> answer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(Json(answer(request.RequestUri!), status));
    }

    /// <summary>
    /// For a fake server whose requests should go several at a time: each one waits (5 s at
    /// most) until another one runs too, which proves they overlap without timing.
    /// </summary>
    public sealed class OverlapProbe
    {
        private readonly Lock _gate = new();
        private readonly TaskCompletionSource _twoAtOnce = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;

        /// <summary>The most requests at once.</summary>
        public int MaxAtOnce { get; private set; }

        public async Task WaitForAnotherAsync(CancellationToken ct)
        {
            lock (_gate)
            {
                MaxAtOnce = Math.Max(MaxAtOnce, ++_inFlight);
                if (_inFlight >= 2)
                    _twoAtOnce.TrySetResult();
            }
            try
            {
                await Task.WhenAny(_twoAtOnce.Task, Task.Delay(TimeSpan.FromSeconds(5), ct));
            }
            finally
            {
                lock (_gate)
                    _inFlight--;
            }
        }
    }

    /// <summary>A throwaway folder, deleted when the test ends (use with "using").</summary>
    public sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "gameradar-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
