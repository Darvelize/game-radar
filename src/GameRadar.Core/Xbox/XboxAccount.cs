using GameRadar.Core.Settings;

namespace GameRadar.Core.Xbox;

/// <summary>What is kept between sessions to avoid signing in at each launch.</summary>
internal sealed record SavedXboxAccount(string RefreshToken, string? Gamertag);

/// <summary>
/// The user's Xbox Live session: sign-in in the browser, saved session,
/// and valid credentials on demand (renewed silently when they expire).
/// </summary>
public sealed class XboxAccount
{
    // Long enough to type a password and a 2-step code, short enough that a
    // forgotten browser tab does not leave the app waiting forever.
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly XboxLiveAuth _auth;
    private readonly SecretStore _store;
    // Credentials are renewed one caller at a time: two parallel renewals
    // would each rotate the refresh token, and one would save a stale one.
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SavedXboxAccount? _saved;
    // Memory only: they last ~16 h, and a new launch renews them silently.
    private XboxCredentials? _credentials;
    // Ended by each sign-out: a renewal or sign-in started before never saves.
    private readonly SessionGuard _session = new();

    public XboxAccount(HttpClient http, SecretStore store)
        : this(new XboxLiveAuth(http), store)
    {
    }

    internal XboxAccount(XboxLiveAuth auth, SecretStore store)
    {
        _auth = auth;
        _store = store;
        _saved = store.Load(Secret.XboxAccount, XboxJsonContext.Default.SavedXboxAccount) is { RefreshToken.Length: > 0 } saved ? saved : null;
    }

    public bool IsSignedIn => _saved is not null;

    public string? Gamertag => _saved?.Gamertag;

    /// <summary>
    /// Opens the Microsoft sign-in page in the browser and waits (5 min max)
    /// for the user to come back, then saves the session.
    /// </summary>
    /// <param name="openBrowser">Opens a web page: a UI concern, given by the app.</param>
    public async Task SignInAsync(Action<Uri> openBrowser, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(openBrowser);

        int session = _session.Current;
        string verifier = Pkce.CreateVerifier();
        string state = Pkce.CreateVerifier();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SignInTimeout);

        using var loopback = LoopbackListener.Start();
        openBrowser(_auth.BuildAuthorizeUri(loopback.RedirectUri, Pkce.ChallengeOf(verifier), state));

        string code;
        try
        {
            code = await loopback.WaitForCodeAsync(state, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our timeout, not the user's "Annuler".
            throw new XboxLiveException(CoreStrings.Xbox_SignInTimeout);
        }

        var tokens = await _auth.RedeemCodeAsync(code, loopback.RedirectUri, verifier, ct).ConfigureAwait(false);
        if (tokens.RefreshToken is null)
            throw new XboxLiveException(CoreStrings.Xbox_NoOfflineAccess);

        var credentials = await _auth.GetXboxCredentialsAsync(tokens.AccessToken, ct).ConfigureAwait(false);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another account maybe: its own gamertag or none, never the
            // previous account's.
            Remember(session, tokens.RefreshToken, credentials.Gamertag, credentials);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Forgets the session on this computer (the history cache is kept).</summary>
    public void SignOut()
    {
        _session.End();
        _store.Delete(Secret.XboxAccount);
        ForgetMemory();
    }

    private void ForgetMemory()
    {
        _saved = null;
        _credentials = null;
    }

    /// <summary>
    /// Valid credentials: from memory, or renewed with the saved refresh token
    /// (3 requests, no browser). Throws <see cref="XboxLiveException"/> with
    /// <see cref="XboxLiveException.RequiresSignIn"/> when a new sign-in is needed.
    /// </summary>
    internal async Task<XboxCredentials> GetCredentialsAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // A margin, so the token cannot expire between here and the call.
            if (_credentials is { } current && current.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
                return current;

            if (_saved is not { } saved)
                throw new XboxLiveException(CoreStrings.Xbox_SignInToLoad, requiresSignIn: true);

            int session = _session.Current;
            MicrosoftTokens tokens;
            try
            {
                tokens = await _auth.RefreshAsync(saved.RefreshToken, ct).ConfigureAwait(false);
            }
            catch (XboxLiveException ex) when (ex.RequiresSignIn)
            {
                // The saved session is dead: drop it, the UI asks for a sign-in.
                SignOut();
                throw;
            }

            // Microsoft usually rotates the refresh token (keep the old one
            // otherwise): saved at once, the old one may be dead already,
            // even if the Xbox Live steps below fail.
            string refreshToken = tokens.RefreshToken ?? saved.RefreshToken;
            Remember(session, refreshToken, saved.Gamertag, credentials: null);

            var credentials = await _auth.GetXboxCredentialsAsync(tokens.AccessToken, ct).ConfigureAwait(false);
            // The same account: its gamertag stays if Xbox Live sends none.
            Remember(session, refreshToken, credentials.Gamertag ?? saved.Gamertag, credentials);
            return credentials;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Titlehub refused the credentials: renew them on the next call.</summary>
    internal void ForgetCredentials() => _credentials = null;

    /// <summary>
    /// Saves the session started under <paramref name="session"/>, unless the
    /// user signed out since: then nothing stays, and a new sign-in is asked.
    /// </summary>
    private void Remember(int session, string refreshToken, string? gamertag, XboxCredentials? credentials)
    {
        var saved = new SavedXboxAccount(refreshToken, gamertag);
        _session.TrySave(_store, Secret.XboxAccount, saved, XboxJsonContext.Default.SavedXboxAccount, session);
        if (!_session.Keep(session, () => { _saved = saved; _credentials = credentials ?? _credentials; }, ForgetMemory))
            throw new XboxLiveException(CoreStrings.Xbox_SignInToLoad, requiresSignIn: true);
    }
}
