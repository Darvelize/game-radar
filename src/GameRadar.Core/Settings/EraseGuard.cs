namespace GameRadar.Core.Settings;

/// <summary>
/// "Effacer toutes mes données" has happened: no store writes a file anymore.
/// One instance is shared by every store of the app (<see cref="DataFolder.Guard"/>),
/// so a single erasure stops them all, even a write already under way on
/// another thread (a cancelled lookup still saves what it found, a picture
/// still arrives). The app quits after an erasure; only a FAILED erasure (a
/// file opened by another program) leaves it open, and then the erasure
/// lowers the guard again (<see cref="Reset"/>): the player keeps saving their
/// choices while they close the other program and retry. Not static on
/// purpose: each test builds its own, and one erasing test cannot block the next.
/// </summary>
public sealed class EraseGuard
{
    // volatile: read by writes running on other threads.
    private volatile bool _erased;

    public bool IsErased => _erased;

    public void Trip() => _erased = true;

    internal void Reset() => _erased = false;
}
