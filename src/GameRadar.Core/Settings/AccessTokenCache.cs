using GameRadar.Core.Services;

namespace GameRadar.Core.Settings;

/// <summary>
/// An account's access token, in memory only (never saved nor logged: the
/// account saves what renews it), shared by the sessions that renew one
/// (Epic, PlayStation). One renewal at a time: two in parallel would each sign
/// in again for nothing, or lose a refresh token replaced at each use (Epic).
/// The account's own steps (renew, sign in again, save) run under the same lock.
/// </summary>
internal sealed class AccessTokenCache
{
    // A margin, so the token cannot expire between its reading and the call.
    internal static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _lock = new(1, 1);
    // One object, replaced whole: read and written from several threads
    // (several lists fetched together), never half written.
    private Entry? _current;

    private sealed record Entry(string Token, DateTimeOffset ExpiresAt);

    /// <summary>
    /// A valid token: the one in memory, else <paramref name="renew"/>'s (it calls
    /// <see cref="Set"/> once the new one is saved), one caller at a time.
    /// </summary>
    public async Task<string> GetAsync(Func<Task<string>> renew, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _current) is { } current && current.ExpiresAt > DateTimeOffset.UtcNow + Margin)
                return current.Token;
            return await renew().ConfigureAwait(false);
        }
        catch (ServiceException ex) when (!ex.IsRateLimited)
        {
            // Every next request needs the session: a loop over games stops here
            // (StoreLookup.FetchEachAsync) instead of renewing again for each one.
            ex.IsSignInError = true;
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Runs <paramref name="step"/> under the renewal lock (a sign-in being saved).</summary>
    public async Task RunLockedAsync(Action step, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            step();
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Set(string token, DateTimeOffset expiresAt) => Volatile.Write(ref _current, new Entry(token, expiresAt));

    /// <summary>
    /// The service refused this token: renewed at the next call. Only if it is
    /// still the current one: a 401 on an old token must not wipe a newer one.
    /// </summary>
    public void Forget(string refusedToken)
    {
        var current = Volatile.Read(ref _current);
        if (current?.Token == refusedToken)
            Interlocked.CompareExchange(ref _current, null, current);
    }

    /// <summary>Signed out: no token any more.</summary>
    public void Clear() => Volatile.Write(ref _current, null);
}
