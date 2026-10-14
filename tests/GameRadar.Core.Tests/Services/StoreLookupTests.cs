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
}
