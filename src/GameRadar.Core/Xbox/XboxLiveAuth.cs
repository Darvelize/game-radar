using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using GameRadar.Core.Services;

namespace GameRadar.Core.Xbox;

/// <summary>Microsoft account tokens. The refresh token may be missing on a refresh.</summary>
internal sealed record MicrosoftTokens(string AccessToken, string? RefreshToken);

/// <summary>What titlehub needs: the XSTS token, its user hash and the account's XUID.</summary>
internal sealed record XboxCredentials(string UserHash, string XstsToken, string Xuid, string? Gamertag, DateTimeOffset ExpiresAt)
{
    public string AuthorizationHeader => $"XBL3.0 x={UserHash};{XstsToken}";
}

/// <summary>
/// The sign-in chain, one HTTP exchange per step, without any UI:
/// Microsoft account (OAuth 2.0) → Xbox user token → XSTS token.
/// </summary>
internal sealed class XboxLiveAuth
{
    /// <summary>
    /// "Application (client) ID" of GameRadar, registered on Microsoft Entra
    /// for personal Microsoft accounts. Not a secret: a desktop app is a
    /// "public client", protected by PKCE and by the registered localhost
    /// redirect, not by a hidden key.
    /// </summary>
    public const string DefaultClientId = "bd8f6bbf-2cb1-44ac-9f58-9413bfb9b119";

    internal const string Scope = "XboxLive.signin offline_access";
    private const string AuthorizeEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string UserTokenEndpoint = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";

    /// <summary>
    /// OAuth error of Microsoft Entra when the app itself is refused: disabled
    /// (AADSTS7000112, AADSTS70001) or no longer found (AADSTS700016), i.e.
    /// Microsoft switched GameRadar off for everyone. Signing in again cannot
    /// help, and the saved session is kept in case access comes back.
    /// </summary>
    internal const string AppBlockedError = "unauthorized_client";


    private readonly HttpClient _http;
    private readonly string _clientId;

    public XboxLiveAuth(HttpClient http, string clientId = DefaultClientId)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _clientId = clientId;
    }

    /// <summary>The Microsoft sign-in page, opened in the user's browser.</summary>
    public Uri BuildAuthorizeUri(string redirectUri, string codeChallenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scope,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            // Lets the user pick the account holding their Xbox profile.
            ["prompt"] = "select_account"
        };
        return new Uri(AuthorizeEndpoint + "?" + string.Join("&",
            query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")));
    }

    /// <summary>Trades the one-time code received on localhost for tokens.</summary>
    public Task<MicrosoftTokens> RedeemCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct) =>
        RequestTokensAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier
        }, renewing: false, ct);

    /// <summary>New tokens from the saved refresh token, without opening the browser.</summary>
    public Task<MicrosoftTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
        RequestTokensAsync(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, renewing: true, ct);

    /// <param name="renewing">True for a saved session (its end means "sign in again"), false for a first sign-in.</param>
    private async Task<MicrosoftTokens> RequestTokensAsync(Dictionary<string, string> form, bool renewing, CancellationToken ct)
    {
        form["client_id"] = _clientId;
        form["scope"] = Scope;

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var answer = await HttpJson.ReadOrNullAsync(response, XboxJsonContext.Default.MicrosoftTokenResponse, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(answer?.AccessToken))
            return new MicrosoftTokens(answer.AccessToken, answer.RefreshToken);

        // Revoked, expired (90 days unused), password changed, a new consent
        // or a second factor asked...: only a new sign-in in the browser can
        // fix a saved session. For a first sign-in (a code refused), that is
        // a sign-in failure, not an expired session: told below.
        if (renewing && answer?.Error is "invalid_grant" or "interaction_required" or "consent_required" or "login_required")
            throw new XboxLiveException(CoreStrings.Xbox_SessionExpired) { RequiresSignIn = true };

        if (answer?.Error == AppBlockedError)
            throw new XboxLiveException(CoreStrings.Xbox_AppBlocked);

        throw new XboxLiveException(
            CoreText.Format(CoreStrings.Xbox_SignInFailed,
                answer?.Error ?? CoreText.Format(CoreStrings.Xbox_ErrorCode, (int)response.StatusCode)));
    }

    /// <summary>Microsoft access token → Xbox user token → XSTS token (2 requests).</summary>
    public async Task<XboxCredentials> GetXboxCredentialsAsync(string microsoftAccessToken, CancellationToken ct)
    {
        var userRequest = new XboxUserTokenRequest();
        userRequest.Properties.RpsTicket = "d=" + microsoftAccessToken;

        using var userResponse = await PostXboxAsync(UserTokenEndpoint, userRequest, XboxJsonContext.Default.XboxUserTokenRequest, ct).ConfigureAwait(false);
        var user = await HttpJson.ReadOrNullAsync(userResponse, XboxJsonContext.Default.XboxTokenResponse, ct).ConfigureAwait(false);
        if (!userResponse.IsSuccessStatusCode || string.IsNullOrEmpty(user?.Token))
            throw new XboxLiveException(CoreText.Format(CoreStrings.Xbox_UserAuthRefused, (int)userResponse.StatusCode));

        var xstsRequest = new XstsTokenRequest();
        xstsRequest.Properties.UserTokens.Add(user.Token);

        using var xstsResponse = await PostXboxAsync(XstsEndpoint, xstsRequest, XboxJsonContext.Default.XstsTokenRequest, ct).ConfigureAwait(false);
        if (!xstsResponse.IsSuccessStatusCode)
        {
            var refusal = await HttpJson.ReadOrNullAsync(xstsResponse, XboxJsonContext.Default.XstsErrorResponse, ct).ConfigureAwait(false);
            throw new XboxLiveException(XstsRefusalMessage(refusal?.XErr, (int)xstsResponse.StatusCode));
        }

        var xsts = await HttpJson.ReadOrNullAsync(xstsResponse, XboxJsonContext.Default.XboxTokenResponse, ct).ConfigureAwait(false);
        var claims = xsts?.DisplayClaims?.Xui?.FirstOrDefault();
        if (string.IsNullOrEmpty(xsts?.Token) || string.IsNullOrEmpty(claims?.UserHash) || string.IsNullOrEmpty(claims.Xuid))
            throw new XboxLiveException(CoreStrings.Xbox_Format);

        return new XboxCredentials(
            claims.UserHash, xsts.Token, claims.Xuid, claims.Gamertag,
            // XSTS tokens last ~16 h; assume 1 h if the date is missing.
            xsts.NotAfter ?? DateTimeOffset.UtcNow.AddHours(1));
    }

    private async Task<HttpResponseMessage> PostXboxAsync<T>(string url, T body, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body, typeInfo)
        };
        request.Headers.Add("x-xbl-contract-version", "1");
        request.Headers.Add("Accept", "application/json");
        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    // Documented XSTS refusal codes ("XErr"), turned into actionable messages.
    internal static string XstsRefusalMessage(long? xerr, int statusCode) => xerr switch
    {
        2148916233 => CoreStrings.Xbox_NoProfile,
        2148916235 => CoreStrings.Xbox_CountryUnavailable,
        2148916236 or 2148916237 => CoreStrings.Xbox_AgeCheck,
        2148916238 => CoreStrings.Xbox_ChildAccount,
        null => CoreText.Format(CoreStrings.Xbox_Refused, statusCode),
        _ => CoreText.Format(CoreStrings.Xbox_RefusedCode, xerr)
    };
}
