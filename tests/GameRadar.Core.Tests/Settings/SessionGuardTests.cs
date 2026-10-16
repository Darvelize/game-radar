using GameRadar.Core.Epic;
using GameRadar.Core.Settings;
using static GameRadar.Core.Tests.TestData;

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

    // The store's folder is a file: every save fails, as on a locked disk.
    private static void SaveOnARefusingDisk(bool renewal)
    {
        using var folder = new TempFolder();
        string notAFolder = Path.Combine(Directory.CreateDirectory(folder.Path).FullName, "file");
        File.WriteAllText(notAFolder, "");
        var guard = new SessionGuard();
        guard.TrySave(new SecretStore(notAFolder), Secret.EpicAccount, new SavedEpicAccount("refresh", "account"),
            EpicJsonContext.Default.SavedEpicAccount, guard.Current, renewal);
    }

    [Fact]
    public void TrySave_ARenewalTheDiskRefuses_GoesOnWithItsTokenInMemory()
    {
        var error = Record.Exception(() => SaveOnARefusingDisk(renewal: true));

        Assert.Null(error);
    }

    [Fact]
    public void TrySave_ASignInTheDiskRefuses_Fails()
    {
        Assert.ThrowsAny<IOException>(() => SaveOnARefusingDisk(renewal: false));
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
