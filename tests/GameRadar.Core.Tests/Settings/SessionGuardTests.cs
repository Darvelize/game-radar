using GameRadar.Core.Settings;

namespace GameRadar.Core.Tests.Settings;

public class SessionGuardTests
{
    [Fact]
    public void Keep_InTheSameSession_KeepsTheMemory()
    {
        var guard = new SessionGuard();
        int session = guard.Current;
        bool kept = false, forgotten = false;

        Assert.True(guard.Keep(session, () => kept = true, () => forgotten = true));

        Assert.True(kept);
        Assert.False(forgotten);
    }

    [Fact]
    public void Keep_ASignOutWhileTheSessionIsKept_ForgetsIt()
    {
        // The sign-out falls right after the secret was saved, while the account
        // fills its memory (saved account, access token): the sign-out cleared them
        // first, so they must be cleared again, not left as a connected account.
        var guard = new SessionGuard();
        int session = guard.Current;
        bool forgotten = false;

        Assert.False(guard.Keep(session, guard.End, () => forgotten = true));

        Assert.True(forgotten);
    }

    [Fact]
    public void Keep_AnEndedSession_KeepsNothing()
    {
        var guard = new SessionGuard();
        int session = guard.Current;
        guard.End();
        bool kept = false;

        Assert.False(guard.Keep(session, () => kept = true, () => { }));

        Assert.False(kept);
    }
}
