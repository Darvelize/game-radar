using System.Text.Json.Serialization;

namespace GameRadar.Core.Xbox;

// Raw JSON shapes of Microsoft sign-in and Xbox Live.
// These classes are internal on purpose: the rest of the app only sees
// PlayedGame, so if the API changes, only this folder has to change.
// Every property is nullable because unofficial APIs often omit fields.
// Names are always explicit ([JsonPropertyName]): Xbox Live compares them
// case-sensitively ("RelyingParty", not "relyingParty").

// ---------- Title history (titlehub) ----------

// titlehub answers { "xuid", "titles" }. Caches written when the app used
// OpenXBL hold the same payload wrapped in { "content": ..., "code": 200 }:
// Content lets the parser read both.
internal sealed class TitleHistoryResponse
{
    [JsonPropertyName("xuid")]
    public string? Xuid { get; set; }

    [JsonPropertyName("titles")]
    public List<TitleDto>? Titles { get; set; }

    [JsonPropertyName("content")]
    public TitleHistoryResponse? Content { get; set; }
}

internal sealed class TitleDto
{
    [JsonPropertyName("titleId")]
    public string? TitleId { get; set; }

    [JsonPropertyName("pfn")]
    public string? PackageFamilyName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    // "Game" for games; apps (Netflix, YouTube...) have another value.
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("devices")]
    public List<string>? Devices { get; set; }

    [JsonPropertyName("achievement")]
    public AchievementSummaryDto? Achievement { get; set; }

    [JsonPropertyName("titleHistory")]
    public TitleHistoryDto? TitleHistory { get; set; }
}

internal sealed class AchievementSummaryDto
{
    [JsonPropertyName("currentAchievements")]
    public int CurrentAchievements { get; set; }

    // Often 0 even when the game has achievements (seen on most recent
    // titles): only trust it when it is greater than 0.
    [JsonPropertyName("totalAchievements")]
    public int TotalAchievements { get; set; }

    [JsonPropertyName("currentGamerscore")]
    public int CurrentGamerscore { get; set; }

    [JsonPropertyName("totalGamerscore")]
    public int TotalGamerscore { get; set; }

    [JsonPropertyName("progressPercentage")]
    public double ProgressPercentage { get; set; }
}

internal sealed class TitleHistoryDto
{
    [JsonPropertyName("lastTimePlayed")]
    public DateTimeOffset? LastTimePlayed { get; set; }
}

// ---------- Microsoft account (OAuth 2.0 token endpoint) ----------

internal sealed class MicrosoftTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    // Rotates: every refresh returns a new one, which replaces the saved one.
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    // "invalid_grant" when the refresh token is revoked or expired.
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

// ---------- Xbox Live user token and XSTS token ----------

internal sealed class XboxUserTokenRequest
{
    [JsonPropertyName("RelyingParty")]
    public string RelyingParty { get; set; } = "http://auth.xboxlive.com";

    [JsonPropertyName("TokenType")]
    public string TokenType { get; set; } = "JWT";

    [JsonPropertyName("Properties")]
    public UserTokenProperties Properties { get; set; } = new();

    internal sealed class UserTokenProperties
    {
        [JsonPropertyName("AuthMethod")]
        public string AuthMethod { get; set; } = "RPS";

        [JsonPropertyName("SiteName")]
        public string SiteName { get; set; } = "user.auth.xboxlive.com";

        // "d=" + access token: the prefix marks a token from an app registered
        // on Microsoft Entra (the "t=" prefix is for older live.com tokens).
        [JsonPropertyName("RpsTicket")]
        public string RpsTicket { get; set; } = "";
    }
}

internal sealed class XstsTokenRequest
{
    // The services on xboxlive.com (titlehub...) accept tokens issued for
    // this relying party.
    [JsonPropertyName("RelyingParty")]
    public string RelyingParty { get; set; } = "http://xboxlive.com";

    [JsonPropertyName("TokenType")]
    public string TokenType { get; set; } = "JWT";

    [JsonPropertyName("Properties")]
    public XstsProperties Properties { get; set; } = new();

    internal sealed class XstsProperties
    {
        [JsonPropertyName("UserTokens")]
        public List<string> UserTokens { get; set; } = [];

        [JsonPropertyName("SandboxId")]
        public string SandboxId { get; set; } = "RETAIL";
    }
}

// Same answer shape for the user token and the XSTS token.
internal sealed class XboxTokenResponse
{
    [JsonPropertyName("Token")]
    public string? Token { get; set; }

    [JsonPropertyName("NotAfter")]
    public DateTimeOffset? NotAfter { get; set; }

    [JsonPropertyName("DisplayClaims")]
    public DisplayClaimsDto? DisplayClaims { get; set; }

    internal sealed class DisplayClaimsDto
    {
        [JsonPropertyName("xui")]
        public List<XboxUserClaims>? Xui { get; set; }
    }

    internal sealed class XboxUserClaims
    {
        // "User hash", paired with the XSTS token in the Authorization header.
        [JsonPropertyName("uhs")]
        public string? UserHash { get; set; }

        // XUID: the account's Xbox user id (XSTS token only).
        [JsonPropertyName("xid")]
        public string? Xuid { get; set; }

        [JsonPropertyName("gtg")]
        public string? Gamertag { get; set; }
    }
}

// XSTS refusal (401), e.g. { "XErr": 2148916233 } for an account without Xbox profile.
internal sealed class XstsErrorResponse
{
    [JsonPropertyName("XErr")]
    public long? XErr { get; set; }
}
