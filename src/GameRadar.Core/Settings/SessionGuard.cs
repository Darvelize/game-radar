using System.Text.Json.Serialization.Metadata;

namespace GameRadar.Core.Settings;

/// <summary>
/// Guards the saving of an account's session against a sign-out made on
/// another thread meanwhile. A sign-out ends the current session (a counter,
/// no lock: the UI must not wait for a renewal on the network), and a renewal
/// or a sign-in started under an ended session never saves its secret, even
/// when the sign-out falls between its check and its save.
/// </summary>
internal sealed class SessionGuard
{
    private int _session;

    /// <summary>The current session: read before a renewal or a sign-in starts.</summary>
    public int Current => Volatile.Read(ref _session);

    /// <summary>Signed out: what was started before must not save any more.</summary>
    public void End() => Interlocked.Increment(ref _session);

    /// <summary>
    /// Saves the secret of <paramref name="session"/>, unless that session
    /// ended: then nothing stays on disk (and <see cref="Keep"/> refuses).
    /// </summary>
    /// <param name="renewal">
    /// A renewal: a disk that refuses the save (a backup, an antivirus) is let
    /// go, the service may have replaced the old token already, so the new one
    /// stays in memory and is saved again at the next renewal rather than lost.
    /// A sign-in fails instead: kept in memory only, the session would be lost
    /// without a word at the next launch.
    /// </param>
    public void TrySave<T>(SecretStore store, Secret secret, T value, JsonTypeInfo<T> typeInfo, int session, bool renewal)
    {
        if (session != Current)
            return;

        try
        {
            store.Save(secret, value, typeInfo);
        }
        catch (Exception ex) when (renewal && ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        // Ended between the check and the save: erased again.
        if (session != Current)
            store.Delete(secret);
    }

    /// <summary>
    /// Once the secret of <paramref name="session"/> is saved: fills the account's memory
    /// (<paramref name="keep"/>: saved account, access token), unless the session ended. A
    /// sign-out that falls while it is filled already cleared that memory: <paramref name="forget"/>
    /// clears it again, rather than leaving a signed-out account connected. False then.
    /// </summary>
    public bool Keep(int session, Action keep, Action forget)
    {
        if (session != Current)
            return false;
        keep();
        if (session == Current)
            return true;
        forget();
        return false;
    }
}
