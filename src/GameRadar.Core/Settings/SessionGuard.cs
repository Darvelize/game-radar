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
    /// ended: then nothing stays on disk, and false is returned.
    /// </summary>
    public bool TrySave<T>(SecretStore store, Secret secret, T value, JsonTypeInfo<T> typeInfo, int session)
    {
        if (session != Current)
            return false;

        store.Save(secret, value, typeInfo);
        if (session == Current)
            return true;
        // Ended between the check and the save: erased again.
        store.Delete(secret);
        return false;
    }
}
