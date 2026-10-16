using System.Net.Http.Headers;
using System.Text;
using GameRadar.Core.Services;
using GameRadar.Core.Settings;

namespace GameRadar.Core.Epic;

/// <summary>
/// What is kept between sessions: the refresh token (valid a year, the year
/// starting again at each use, and replaced by a new one each time) and the
/// account it belongs to. A secret: encrypted on disk.
/// </summary>
internal sealed record SavedEpicAccount(string RefreshToken, string AccountId);

/// <summary>
/// The user's Epic session: an authorization code pasted once (Epic offers no
/// sign-in for third-party apps), then a valid access token on demand.
/// </summary>
public sealed class EpicAccount
{
    private const string TokenUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token";

    private readonly HttpClient _http;
    private readonly SecretStore _store;
    // The access token (36 h): see AccessTokenCache.
    private readonly AccessTokenCache _tokens = new();

    private SavedEpicAccount? _saved;
    // Ended by each disconnect: a renewal or sign-in started before never saves.
    private readonly SessionGuard _session = new();

    public EpicAccount(HttpClient http, SecretStore store)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _store = store;
        _saved = store.Load(Secret.EpicAccount, EpicJsonContext.Default.SavedEpicAccount) is { RefreshToken.Length: > 0 } saved ? saved : null;
    }

    public bool IsConnected => _saved is not null;

    /// <summary>The account the play time and achievements are asked for.</summary>
    internal string? AccountId => _saved?.AccountId;

    /// <summary>
    /// Exchanges the pasted code (1 request), then saves the session. Throws
    /// <see cref="EpicException"/> with an explanation when Epic refuses it.
    /// </summary>
    /// <param name="pasted">The code, or the whole JSON of the page that shows it.</param>
    public async Task ConnectAsync(string pasted, CancellationToken ct = default)
    {
        string code = EpicClient.RequireCode(pasted);
        int session = _session.Current;

        var tokens = await RequestTokensAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
        }, exchange: true, ct).ConfigureAwait(false);

        await _tokens.RunLockedAsync(() => Remember(session, tokens, renewal: false), ct).ConfigureAwait(false);
    }

    /// <summary>Forgets the session on this computer (the history cache is kept).</summary>
    public void Disconnect()
    {
        _session.End();
        _store.Delete(Secret.EpicAccount);
        ForgetMemory();
    }

    private void ForgetMemory()
    {
        _saved = null;
        _tokens.Clear();
    }

    /// <summary>
    /// A valid access token: from memory, else renewed with the refresh token
    /// (no question asked). Throws <see cref="EpicException"/> with
    /// <see cref="EpicException.IsTokenRefused"/> when a new code is needed.
    /// </summary>
    internal Task<string> GetAccessTokenAsync(CancellationToken ct) => _tokens.GetAsync(() => RenewAsync(ct), ct);

    // Under the lock of _tokens.
    private async Task<string> RenewAsync(CancellationToken ct)
    {
        if (_saved is not { } saved)
            throw EpicException.SessionExpired();

        int session = _session.Current;
        EpicTokens tokens;
        try
        {
            tokens = await RequestTokensAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = saved.RefreshToken,
            }, exchange: false, ct).ConfigureAwait(false);
        }
        catch (EpicException ex) when (ex.IsTokenRefused)
        {
            // Expired (a year unused) or revoked: drop it, the UI asks for a new code.
            Disconnect();
            throw;
        }

        Remember(session, tokens, renewal: true);
        return tokens.AccessToken;
    }

    /// <summary>
    /// Epic refused this access token: renew it on the next call. Only if it
    /// is still the current one: a 401 on an old token must not wipe a newer one.
    /// </summary>
    internal void ForgetAccessToken(string refusedToken) => _tokens.Forget(refusedToken);

    /// <summary>The token endpoint (eg1 tokens), with the Epic launcher's client.</summary>
    /// <param name="exchange">A pasted code: any refusal means "paste a new one". A renewal only drops the kept session when Epic says the token is invalid.</param>
    private async Task<EpicTokens> RequestTokensAsync(Dictionary<string, string> form, bool exchange, CancellationToken ct)
    {
        form["token_type"] = "eg1";
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(EpicClient.ClientId + ":" + EpicClient.ClientSecret)));
        request.Headers.UserAgent.ParseAdd(EpicClient.LauncherUserAgent);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return HttpOutcomes.Classify(response) switch
        {
            HttpOutcome.Ok => EpicClient.ParseTokens(body, DateTimeOffset.UtcNow),
            HttpOutcome.BadRequest or HttpOutcome.Unauthorized when exchange || EpicClient.IsGrantRefused(body)
                => throw (exchange ? new EpicException(CoreStrings.Epic_CodeRefused) { IsTokenRefused = true } : EpicException.SessionExpired()),
            HttpOutcome.RateLimited => throw EpicException.RateLimited(),
            // Anything else (a firewall, terms to accept on epicgames.com, the
            // launcher's client changed): the kept session is not thrown away.
            _ => throw new EpicException(CoreStrings.Epic_Down)
        };
    }

    /// <summary>
    /// Saves the session started under <paramref name="session"/> (the new
    /// refresh token replaces the old one, no longer valid), unless the user
    /// disconnected since: then nothing stays, and a new code is asked.
    /// </summary>
    private void Remember(int session, EpicTokens tokens, bool renewal)
    {
        var saved = new SavedEpicAccount(tokens.RefreshToken, tokens.AccountId);
        _session.TrySave(_store, Secret.EpicAccount, saved, EpicJsonContext.Default.SavedEpicAccount, session, renewal);
        if (!_session.Keep(session, () => { _saved = saved; _tokens.Set(tokens.AccessToken, tokens.ExpiresAt); }, ForgetMemory))
            throw EpicException.SessionExpired();
    }
}
