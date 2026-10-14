using GameRadar.Core.Settings;

namespace GameRadar.Core.Tests.Settings;

public class AccessTokenCacheTests
{
    private static readonly DateTimeOffset InAnHour = DateTimeOffset.UtcNow.AddHours(1);

    [Fact]
    public async Task TwoCallersAtOnce_RenewOnlyOnce()
    {
        var tokens = new AccessTokenCache();
        int renewals = 0;
        var release = new TaskCompletionSource();
        async Task<string> Renew()
        {
            renewals++;
            await release.Task;
            tokens.Set("token", InAnHour);
            return "token";
        }

        var first = tokens.GetAsync(Renew, CancellationToken.None);
        var second = tokens.GetAsync(Renew, CancellationToken.None);
        release.SetResult();

        Assert.Equal(["token", "token"], await Task.WhenAll(first, second));
        Assert.Equal(1, renewals);
    }

    [Fact]
    public async Task ATokenAboutToExpire_IsRenewed()
    {
        // Within the 5-minute margin: it could expire between here and the call.
        var tokens = new AccessTokenCache();
        tokens.Set("old", DateTimeOffset.UtcNow.AddMinutes(4));

        Assert.Equal("new", await tokens.GetAsync(() => Task.FromResult("new"), CancellationToken.None));
    }

    [Fact]
    public async Task Forget_OnlyForgetsTheTokenThatWasRefused()
    {
        var tokens = new AccessTokenCache();
        tokens.Set("current", InAnHour);
        Task<string> Renew() => Task.FromResult("renewed");

        // A 401 on an older token: the current one stays.
        tokens.Forget("older");
        Assert.Equal("current", await tokens.GetAsync(Renew, CancellationToken.None));

        tokens.Forget("current");
        Assert.Equal("renewed", await tokens.GetAsync(Renew, CancellationToken.None));
    }

    [Fact]
    public async Task Clear_ForgetsTheToken()
    {
        var tokens = new AccessTokenCache();
        tokens.Set("current", InAnHour);

        tokens.Clear();

        Assert.Equal("renewed", await tokens.GetAsync(() => Task.FromResult("renewed"), CancellationToken.None));
    }
}
