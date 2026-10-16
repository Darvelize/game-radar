using GameRadar.Core.Epic;
using GameRadar.Core.Services;

namespace GameRadar.Core.Tests.Services;

public class StoreLookupTests
{
    [Fact]
    public async Task FetchAllAsync_KeepsTheOrderOfTheItems_AndAMissingAnswerIsNull()
    {
        var answers = await StoreLookup.FetchAllAsync<int, string?>([1, 2, 3], 2,
            async (n, _) => { await Task.Delay(10 * (4 - n)); return n == 2 ? null : $"#{n}"; },
            () => new InvalidOperationException(), CancellationToken.None);

        Assert.Equal(new string?[] { "#1", null, "#3" }, answers);
    }

    [Fact]
    public async Task FetchAllAsync_NotOneAnswer_IsABrokenQuery()
    {
        // Not one answer among well-known games: the query broke.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => StoreLookup.FetchAllAsync<int, double?>([1, 2], 8,
            (_, _) => Task.FromResult<double?>(null), () => new InvalidOperationException("broken"), CancellationToken.None));

        Assert.Equal("broken", error.Message);
    }

    // The details of a history (achievements, trophies): one rule for every platform.

    public static TheoryData<Exception> AnswersOfOneGameThatFail => new()
    {
        new EpicException("refused for this game"),
        new HttpRequestException("server error", null, System.Net.HttpStatusCode.InternalServerError),
        new System.Text.Json.JsonException("odd answer"),
    };

    [Theory]
    [MemberData(nameof(AnswersOfOneGameThatFail))]
    public async Task FetchEachAsync_AGameWhoseAnswerFails_IsSkippedAndTheOthersAsked(Exception error)
    {
        var asked = new System.Collections.Concurrent.ConcurrentBag<int>();

        await StoreLookup.FetchEachAsync([1, 2, 3, 4], 1, (n, _) =>
        {
            asked.Add(n);
            return n == 2 ? Task.FromException(error) : Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], asked.Order());
    }

    public static TheoryData<Exception> NoAnswer => new()
    {
        new HttpRequestException("unreachable"),
        new TaskCanceledException("the HttpClient timeout"),
    };

    [Theory]
    [MemberData(nameof(NoAnswer))]
    public async Task FetchEachAsync_TheNetworkFailing_StopsTheGamesNotAskedYet(Exception error)
    {
        var asked = new List<int>();

        // Each game would wait as long (30 s): the rest is asked at the next sync.
        await StoreLookup.FetchEachAsync([1, 2, 3, 4], 1, (n, _) =>
        {
            asked.Add(n);
            return n == 2 ? Task.FromException(error) : Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Equal([1, 2], asked);
    }

    public static TheoryData<Exception> StopsEverything => new()
    {
        EpicException.RateLimited(),
        EpicException.SessionExpired(),
        // The session could not be renewed (the service down): every next request needs it.
        new EpicException("sign-in down") { IsSignInError = true },
    };

    [Theory]
    [MemberData(nameof(StopsEverything))]
    public async Task FetchEachAsync_TheRateLimitOrAnEndedSession_GoUp(Exception error)
    {
        var thrown = await Assert.ThrowsAnyAsync<ServiceException>(() => StoreLookup.FetchEachAsync([1, 2, 3, 4], 2,
            (n, _) => n == 2 ? Task.FromException(error) : Task.CompletedTask, CancellationToken.None));

        Assert.Same(error, thrown);
    }
}
