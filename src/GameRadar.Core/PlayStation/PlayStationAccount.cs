using System.Text.RegularExpressions;
using GameRadar.Core.Settings;

namespace GameRadar.Core.PlayStation;

/// <summary>
/// What is kept between sessions. The NPSSO is kept too: the refresh token
/// expires after ~10 days whatever its use, and the NPSSO then signs in
/// again without asking the user. Both are secrets: encrypted on disk.
/// </summary>
internal sealed record SavedPlayStationAccount(string Npsso, string? RefreshToken, DateTimeOffset? RefreshExpiresAt);

/// <summary>
/// The user's PlayStation Network session: the NPSSO they pasted (Sony's
/// session cookie, the only way in: Sony offers no sign-in for third-party
/// apps), and a valid access token on demand.
/// </summary>
public sealed partial class PlayStationAccount
{
    private readonly PlayStationAuth _auth;
    private readonly SecretStore _store;
    // The access token (~1 h): see AccessTokenCache.
    private readonly AccessTokenCache _tokens = new();

    private SavedPlayStationAccount? _saved;
    // Ended by each disconnect: a renewal or sign-in started before never saves.
    private readonly SessionGuard _session = new();

    /// <remarks>No HttpClient to give: the sign-in has its own (<see cref="PlayStationAuth.SignInHttp"/>).</remarks>
    public PlayStationAccount(SecretStore store)
        : this(new PlayStationAuth(PlayStationAuth.SignInHttp), store)
    {
    }

    internal PlayStationAccount(PlayStationAuth auth, SecretStore store)
    {
        _auth = auth;
        _store = store;
        _saved = store.Load(Secret.PlayStationAccount, PlayStationJsonContext.Default.SavedPlayStationAccount) is { Npsso.Length: > 0 } saved ? saved : null;
    }

    public bool IsConnected => _saved is not null;

    /// <summary>
    /// Signs in with the pasted NPSSO (2 requests), then saves the session.
    /// Throws <see cref="PlayStationException"/> with an explanation when Sony refuses it.
    /// </summary>
    /// <param name="pasted">The NPSSO, or the whole answer of the page that shows it ({"npsso":"…"}).</param>
    public async Task ConnectAsync(string pasted, CancellationToken ct = default)
    {
        string npsso = ParseNpsso(pasted) ?? throw new PlayStationException(CoreStrings.PlayStation_PasteNpsso);
        int session = _session.Current;

        var tokens = await _auth.SignInAsync(npsso, ct).ConfigureAwait(false);

        await _tokens.RunLockedAsync(() => Remember(session, npsso, tokens), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The NPSSO in what the user pasted: the value alone, or the JSON of
    /// ca.account.sony.com/api/v1/ssocookie copied whole. Null if none.
    /// </summary>
    internal static string? ParseNpsso(string pasted)
    {
        string text = pasted.Trim();
        if (NpssoJsonPattern().Match(text) is { Success: true } json)
            return json.Groups[1].Value;
        return NpssoPattern().IsMatch(text) ? text : null;
    }

    /// <summary>Forgets the session on this computer (the history cache is kept).</summary>
    public void Disconnect()
    {
        _session.End();
        _store.Delete(Secret.PlayStationAccount);
        ForgetMemory();
    }

    private void ForgetMemory()
    {
        _saved = null;
        _tokens.Clear();
    }

    /// <summary>
    /// A valid access token: from memory, else renewed with the refresh
    /// token, else signed in again with the NPSSO (no question asked).
    /// Throws <see cref="PlayStationException"/> with
    /// <see cref="PlayStationException.RequiresSignIn"/> when a new NPSSO is needed.
    /// </summary>
    internal Task<string> GetAccessTokenAsync(CancellationToken ct) => _tokens.GetAsync(() => RenewAsync(ct), ct);

    // Under the lock of _tokens.
    private async Task<string> RenewAsync(CancellationToken ct)
    {
        // The refresh token too must outlive the call.
        var soon = DateTimeOffset.UtcNow + AccessTokenCache.Margin;
        if (_saved is not { } saved)
            throw new PlayStationException(CoreStrings.PlayStation_NotConnected, requiresSignIn: true);

        int session = _session.Current;
        PlayStationTokens tokens;
        // Its date unknown (Sony did not give it): tried until refused.
        if (saved.RefreshToken is { } refreshToken && (saved.RefreshExpiresAt is null || saved.RefreshExpiresAt > soon))
        {
            try
            {
                tokens = await _auth.RefreshAsync(refreshToken, ct).ConfigureAwait(false);
            }
            catch (PlayStationException)
            {
                // Refused before its date, or failing for any reason of
                // its own: the NPSSO may still sign in.
                tokens = await SignInAgainAsync(saved.Npsso, ct).ConfigureAwait(false);
            }
        }
        else
        {
            tokens = await SignInAgainAsync(saved.Npsso, ct).ConfigureAwait(false);
        }

        Remember(session, saved.Npsso, tokens);
        return tokens.AccessToken;
    }

    /// <summary>
    /// PSN refused this access token: renew it on the next call. Only if it is
    /// still the current one: both lists are fetched together, and a 401 on
    /// an old token must not wipe the one the other list just renewed.
    /// </summary>
    internal void ForgetAccessToken(string refusedToken) => _tokens.Forget(refusedToken);

    private async Task<PlayStationTokens> SignInAgainAsync(string npsso, CancellationToken ct)
    {
        try
        {
            return await _auth.SignInAsync(npsso, ct).ConfigureAwait(false);
        }
        catch (PlayStationException ex) when (ex.RequiresSignIn)
        {
            // The NPSSO is dead too (~2 months, or signed out everywhere):
            // drop the session, the UI asks for a new one.
            Disconnect();
            throw;
        }
    }

    /// <summary>
    /// Saves the session started under <paramref name="session"/>, unless the
    /// user disconnected since: then nothing stays, and a new NPSSO is asked.
    /// </summary>
    private void Remember(int session, string npsso, PlayStationTokens tokens)
    {
        // A refresh may answer without a new refresh token: keep the old one.
        var previous = _saved?.Npsso == npsso ? _saved : null;
        var saved = tokens.RefreshToken is null && previous is not null
            ? previous
            : new SavedPlayStationAccount(npsso, tokens.RefreshToken, tokens.RefreshExpiresAt);
        _session.TrySave(_store, Secret.PlayStationAccount, saved, PlayStationJsonContext.Default.SavedPlayStationAccount, session);
        if (!_session.Keep(session, () => { _saved = saved; _tokens.Set(tokens.AccessToken, tokens.ExpiresAt); }, ForgetMemory))
            throw new PlayStationException(CoreStrings.PlayStation_NotConnected, requiresSignIn: true);
    }

    // {"npsso":"…"}: the page's answer, copied whole.
    [GeneratedRegex(@"""npsso""\s*:\s*""([A-Za-z0-9]+)""")]
    private static partial Regex NpssoJsonPattern();

    // The value alone: 64 letters and digits (a little leeway on the length).
    [GeneratedRegex(@"^[A-Za-z0-9]{32,128}$")]
    private static partial Regex NpssoPattern();
}
