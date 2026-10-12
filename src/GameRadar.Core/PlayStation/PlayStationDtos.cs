using System.Text.Json.Serialization;

namespace GameRadar.Core.PlayStation;

// Raw JSON shapes of Sony's sign-in and of PlayStation Network (unofficial
// APIs of the PlayStation App). Internal on purpose: the rest of the app only
// sees PlayedGame, so if the API changes, only this folder has to change.

// ---------- ca.account.sony.com/api/authz/v3/oauth/token ----------

internal sealed class PlayStationTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    // Seconds (~1 h).
    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    // Seconds (~10 days), and NOT extended by a refresh: the NPSSO signs in again after.
    [JsonPropertyName("refresh_token_expires_in")]
    public int? RefreshTokenExpiresIn { get; set; }

    // "invalid_grant", "invalid_client"...
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

// ---------- trophy/v1/users/me/trophyTitles: every game with trophies ----------

internal sealed class TrophyTitlesPageDto
{
    [JsonPropertyName("trophyTitles")]
    public List<TrophyTitleDto>? TrophyTitles { get; set; }

    [JsonPropertyName("totalItemCount")]
    public int TotalItemCount { get; set; }
}

internal sealed class TrophyTitleDto
{
    // The trophy set ("NPWR12527_00"), shared by the PS4/PS5 versions of a game.
    [JsonPropertyName("npCommunicationId")]
    public string? NpCommunicationId { get; set; }

    // Sometimes "Trophées X" or "Collection de trophées de X" (PlayStationClient.CleanTrophyTitleName).
    [JsonPropertyName("trophyTitleName")]
    public string? TrophyTitleName { get; set; }

    // 0-100, weighted by trophy grade and counting the DLC trophies: a
    // platinum (base game complete) can show 45 %.
    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("earnedTrophies")]
    public TrophyCountsDto? EarnedTrophies { get; set; }

    [JsonPropertyName("definedTrophies")]
    public TrophyCountsDto? DefinedTrophies { get; set; }

    // Last trophy activity: the only date of a PS3/Vita game.
    [JsonPropertyName("lastUpdatedDateTime")]
    public DateTimeOffset? LastUpdatedDateTime { get; set; }
}

internal sealed class TrophyCountsDto
{
    [JsonPropertyName("bronze")]
    public int Bronze { get; set; }

    [JsonPropertyName("silver")]
    public int Silver { get; set; }

    [JsonPropertyName("gold")]
    public int Gold { get; set; }

    [JsonPropertyName("platinum")]
    public int Platinum { get; set; }

    public int Total => Bronze + Silver + Gold + Platinum;
}

// ---------- gamelist/v2/users/me/titles: PS4/PS5 games with play time ----------

internal sealed class GamesPageDto
{
    [JsonPropertyName("titles")]
    public List<PlayedTitleDto>? Titles { get; set; }

    [JsonPropertyName("totalItemCount")]
    public int TotalItemCount { get; set; }
}

internal sealed class PlayedTitleDto
{
    // One version of a game in one region ("CUSA07484_00", PS5: "PPSA…").
    [JsonPropertyName("titleId")]
    public string? TitleId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("localizedName")]
    public string? LocalizedName { get; set; }

    // ISO 8601 duration: "PT159H21M30S".
    [JsonPropertyName("playDuration")]
    public string? PlayDuration { get; set; }

    [JsonPropertyName("lastPlayedDateTime")]
    public DateTimeOffset? LastPlayedDateTime { get; set; }

    [JsonPropertyName("concept")]
    public ConceptDto? Concept { get; set; }
}

internal sealed class ConceptDto
{
    // Coarse PlayStation Store genres: "ACTION", "ROLE_PLAYING_GAMES"...
    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }
}

// ---------- trophy/v1/users/me/titles/trophyTitles?npTitleIds=: game -> trophy set ----------

internal sealed class TitleTrophySetsDto
{
    [JsonPropertyName("titles")]
    public List<TitleTrophySetDto>? Titles { get; set; }
}

internal sealed class TitleTrophySetDto
{
    [JsonPropertyName("npTitleId")]
    public string? NpTitleId { get; set; }

    // Empty for a game without any trophy set.
    [JsonPropertyName("trophyTitles")]
    public List<TrophyTitleDto>? TrophyTitles { get; set; }
}

// ---------- Our cache: the raw pages of both lists, side by side ----------

internal sealed class PlayStationHistoryDto
{
    [JsonPropertyName("trophyTitles")]
    public List<TrophyTitlesPageDto>? TrophyTitles { get; set; }

    [JsonPropertyName("games")]
    public List<GamesPageDto>? Games { get; set; }
}
