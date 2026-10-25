using System.Net;
using System.Net.Http.Headers;
using System.Web;
using GameRadar.Core.Services;

namespace GameRadar.Core.PlayStation;

/// <summary>PSN tokens. The refresh token may be missing on a refresh.</summary>
internal sealed record PlayStationTokens(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string? RefreshToken,
    DateTimeOffset? RefreshExpiresAt);

/// <summary>
/// Sony's sign-in, one HTTP exchange per step, without any UI: the NPSSO
/// (Sony's session cookie, pasted by the user) → a one-time code → tokens.
/// </summary>
/// <remarks>
/// Sony registers no third-party app: like every PSN tool, GameRadar uses
/// the identity of Sony's own "PlayStation App" (Android). An exception to
/// the rule "never reuse a publisher app's identifier", chosen by the user
/// for lack of any other way; Sony may block it at any time (<see cref="IsAppBlocked"/>).
/// </remarks>
internal sealed class PlayStationAuth
{
    private const string ClientId ="09515159-7237-4370-9b40-3806e67c0891";
    // "client_id:client_secret" of the PlayStation App, in HTTP Basic form.
    // Shipped in the app itself and published by every PSN library: not a
    // secret of GameRadar's, nor of the user's.
    private const string ClientCredentials = "MDk1MTUxNTktNzIzNy00MzcwLTliNDAtMzgwNmU2N2MwODkxOnVjUGprYTV0bnRCMktxc1A=";
    internal const string RedirectUri = "com.scee.psxandroid.scecompcall://redirect";
    internal const string Scope = "psn:mobile.v2.core psn:clientapp";
    private const string AuthorizeEndpoint = "https://ca.account.sony.com/api/authz/v3/oauth/authorize";
    private const string TokenEndpoint = "https://ca.account.sony.com/api/authz/v3/oauth/token";

    // The access token's lifetime when Sony does not give it (1 h, measured).
    private const int DefaultAccessLifetimeSeconds = 3600;


    /// <summary>
    /// The sign-in's own HttpClient, the one exception to the app's shared
    /// client: the shared one follows redirects, and would try to follow
    /// Sony's redirect to the PlayStation App's scheme (and fail on its
    /// "port -1"); it also keeps cookies, and Sony's session cookies have
    /// nothing to do there. Created once, for the whole process.
    /// </summary>
    internal static readonly HttpClient SignInHttp = new(CreateSignInHandler());

    internal static SocketsHttpHandler CreateSignInHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(15)
    };

    private readonly HttpClient _http;
    private readonly string _authorizeEndpoint;
    private readonly string _tokenEndpoint;

    /// <param name="baseUrl">Another server than Sony's, for tests only.</param>
    public PlayStationAuth(HttpClient http, string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _authorizeEndpoint = baseUrl is null ? AuthorizeEndpoint : baseUrl + "authorize";
        _tokenEndpoint = baseUrl is null ? TokenEndpoint : baseUrl + "token";
    }

    /// <summary>Signs in with the NPSSO (2 requests).</summary>
    public async Task<PlayStationTokens> SignInAsync(string npsso, CancellationToken ct)
    {
        string code = await RequestCodeAsync(npsso, ct).ConfigureAwait(false);
        return await RequestTokensAsync(new()
        {
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code",
            ["token_format"] = "jwt"
        }, renewing: false, ct).ConfigureAwait(false);
    }

    /// <summary>New tokens from the refresh token, while it lasts (~10 days).</summary>
    public Task<PlayStationTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
        RequestTokensAsync(new()
        {
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["token_format"] = "jwt",
            ["scope"] = Scope
        }, renewing: true, ct);

    // Sony answers with a redirect to the PlayStation App's own scheme,
    // carrying the code: read from its Location header, never followed
    // (see SignInHttp).
    private async Task<string> RequestCodeAsync(string npsso, CancellationToken ct)
    {
        var query = new Dictionary<string, string>
        {
            ["access_type"] = "offline",
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, _authorizeEndpoint + "?" + string.Join("&",
            query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")));
        // The NPSSO is the cookie of the user's Sony session.
        request.Headers.TryAddWithoutValidation("Cookie", "npsso=" + npsso);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var answer = response.Headers.Location is { IsAbsoluteUri: true } location
            ? HttpUtility.ParseQueryString(location.Query)
            : null;

        if (answer?["code"] is { Length: > 0 } code)
            return code;
        if (IsAppBlocked(answer?["error"]))
            throw new PlayStationException(CoreStrings.PlayStation_AppBlocked);
        // Only a redirect says something of the NPSSO: "login_required"
        // (expired, or signed out everywhere), a login page... An outage,
        // a rate limit or an error page is not a dead NPSSO: it must not
        // cost the player their session.
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new PlayStationException(CoreStrings.PlayStation_NpssoRefused) { RequiresSignIn = true };
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw PlayStationException.RateLimited();
        throw new PlayStationException(CoreText.Format(CoreStrings.PlayStation_SignInFailed,
            CoreText.Format(CoreStrings.PlayStation_ErrorCode, (int)response.StatusCode)));
    }

    /// <param name="renewing">True for the refresh token, false for the one-time code of a sign-in.</param>
    private async Task<PlayStationTokens> RequestTokensAsync(Dictionary<string, string> form, bool renewing, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", ClientCredentials);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var answer = await HttpJson.ReadOrNullAsync(response, PlayStationJsonContext.Default.PlayStationTokenResponse, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(answer?.AccessToken))
        {
            return new PlayStationTokens(
                answer.AccessToken,
                // Absent: the lifetime measured (1 h), not "already expired",
                // which would sign in again at every call.
                now.AddSeconds(answer.ExpiresIn ?? DefaultAccessLifetimeSeconds),
                answer.RefreshToken,
                answer.RefreshTokenExpiresIn is { } seconds ? now.AddSeconds(seconds) : null);
        }

        // A dead refresh token: the account signs in again with the NPSSO.
        // On the one-time code of a sign-in (used, expired), it says nothing
        // of the NPSSO: a failure, told below, that keeps the session.
        if (renewing && answer?.Error == "invalid_grant")
            throw new PlayStationException(CoreStrings.PlayStation_NpssoRefused) { RequiresSignIn = true };
        if (IsAppBlocked(answer?.Error))
            throw new PlayStationException(CoreStrings.PlayStation_AppBlocked);

        throw new PlayStationException(
            CoreText.Format(CoreStrings.PlayStation_SignInFailed,
                answer?.Error ?? CoreText.Format(CoreStrings.PlayStation_ErrorCode, (int)response.StatusCode)));
    }

    /// <summary>
    /// OAuth errors when the client itself is refused, i.e. Sony blocked the
    /// PlayStation App identity for GameRadar. A new NPSSO cannot help.
    /// </summary>
    private static bool IsAppBlocked(string? error) => error is "invalid_client" or "unauthorized_client";
}
