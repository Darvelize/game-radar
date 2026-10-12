using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.Xbox;

/// <summary>
/// Connector for Xbox Live's title history ("titlehub"), called directly
/// with the user's own session (<see cref="XboxAccount"/>).
/// Its only job: call the API and translate the answer into PlayedGame.
/// </summary>
public sealed class XboxLiveClient
{
    // The "achievement" decoration adds the progress of each title.
    private const string TitleHistoryUrl =
        "https://titlehub.xboxlive.com/users/xuid({0})/titles/titlehistory/decoration/achievement,image,detail";

    /// <summary>Value of <see cref="PlayedGame.Platform"/> for games from this connector.</summary>
    public const string PlatformName = "Xbox";

    private readonly HttpClient _http;
    private readonly XboxAccount _account;

    /// <param name="http">
    /// Injected rather than created here: an HttpClient should be shared
    /// across the app. Creating one per call can exhaust network sockets.
    /// </param>
    /// <param name="account">The signed-in user's session.</param>
    public XboxLiveClient(HttpClient http, XboxAccount account)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(account);
        _http = http;
        _account = account;
    }

    /// <summary>
    /// Fetches the signed-in player's title history (1 request, plus 3 to
    /// renew the session when it expired) as raw JSON, to be cached as-is and
    /// read with <see cref="ParseJson"/>. Caching the raw text rather than
    /// PlayedGame means improved parsing rules also apply to cached data.
    /// </summary>
    public async Task<string> FetchHistoryJsonAsync(CancellationToken ct = default)
    {
        // One retry with renewed credentials: those in memory may have been
        // revoked early (password changed elsewhere); the user should not
        // have to click again for that.
        for (int attempt = 1; ; attempt++)
        {
            var credentials = await _account.GetCredentialsAsync(ct).ConfigureAwait(false);

            // Headers are set on the request, not on the shared HttpClient,
            // so the same HttpClient can safely serve other APIs.
            using var request = new HttpRequestMessage(HttpMethod.Get,
                string.Format(CultureInfo.InvariantCulture, TitleHistoryUrl, Uri.EscapeDataString(credentials.Xuid)));
            // "XBL3.0 x=hash;token" is not a standard header syntax: added as-is.
            request.Headers.TryAddWithoutValidation("Authorization", credentials.AuthorizationHeader);
            request.Headers.Add("x-xbl-contract-version", "2");
            // Game names in English, like every other source: a game is matched
            // across platforms (and with Steam) by its title.
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (attempt == 1 && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _account.ForgetCredentials();
                continue;
            }
            EnsureSuccess(response);

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Turns the raw history JSON into games, most recently played first.
    /// Also reads caches saved when the app used OpenXBL (same payload,
    /// wrapped in "content"). Throws <see cref="JsonException"/> if the text
    /// is not valid JSON.
    /// </summary>
    public static IReadOnlyList<PlayedGame> ParseJson(string json)
    {
        var root = JsonSerializer.Deserialize(json, XboxJsonContext.Default.TitleHistoryResponse);
        return ParseTitles(root?.Content ?? root);
    }

    /// <summary>
    /// Pure mapping logic, separated from the HTTP call so it can be
    /// tested with a JSON sample, without network or account.
    /// </summary>
    private static IReadOnlyList<PlayedGame> ParseTitles(TitleHistoryResponse? payload)
    {
        if (payload?.Titles is null)
            return Array.Empty<PlayedGame>();

        // The games from other stores are left out here, at parsing: the
        // cache keeps the raw answer, so an old cache is cleaned up too.
        return payload.Titles
            .Where(t => IsGame(t) && !IsFromOtherStore(t))
            .Select(ToPlayedGame)
            .OrderByDescending(g => g.LastPlayed)
            .ToList();
    }

    private static bool IsGame(TitleDto title)
    {
        if (string.IsNullOrWhiteSpace(title.TitleId) || string.IsNullOrWhiteSpace(title.Name))
            return false;

        // When the type is missing, keep the title only if it has achievements
        // (apps never do).
        return title.Type is null
            ? HasAchievements(title.Achievement)
            : title.Type.Equals("Game", StringComparison.OrdinalIgnoreCase);
    }

    // totalAchievements is often 0 on recent games, but totalGamerscore is
    // reliable: every game with achievements is worth some gamerscore.
    // PC games from other stores (Steam...) have 0 for both.
    private static bool HasAchievements(AchievementSummaryDto? a) =>
        a is not null && (a.TotalGamerscore > 0 || a.TotalAchievements > 0);

    private static PlayedGame ToPlayedGame(TitleDto title)
    {
        int unlocked = title.Achievement?.CurrentAchievements ?? 0;
        // 0 means "not reported", not "no achievements": turn it into null.
        int? total = title.Achievement?.TotalAchievements is > 0 and var t ? t : null;
        double percentage = title.Achievement?.ProgressPercentage ?? 0;

        return new PlayedGame(
            Platform: PlatformName,
            PlatformId: title.TitleId!,
            Name: title.Name!.Trim(),
            HasAchievements: HasAchievements(title.Achievement),
            UnlockedAchievements: unlocked,
            TotalAchievements: total,
            ProgressPercentage: percentage,
            LastPlayed: title.TitleHistory?.LastTimePlayed,
            Status: StatusClassifier.FromAchievements(unlocked, total, percentage),
            PackageFamilyName: string.IsNullOrWhiteSpace(title.PackageFamilyName) ? null : title.PackageFamilyName);
    }

    // The Xbox PC app also records games launched from other stores (Steam,
    // Epic...). They are the only titles whose sole device is "Win32";
    // real Xbox games report "PC", "XboxOne", "XboxSeries" or "Xbox360".
    // Left out (choice of the user): no achievements, no play time, nothing
    // about the player's taste. Played on Steam, the Steam library has them;
    // elsewhere, the player can add them by hand. Kept when it does earn Xbox
    // achievements (a Microsoft game bought on Steam): its progress tells.
    private static bool IsFromOtherStore(TitleDto title) =>
        title.Devices is { Count: > 0 } devices
        && devices.All(d => d.Equals("Win32", StringComparison.OrdinalIgnoreCase))
        && !HasAchievements(title.Achievement);

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        // Translate the HTTP codes we understand into messages for the user.
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                // Maybe revoked early: the next attempt renews the credentials.
                _account.ForgetCredentials();
                throw new XboxLiveException(CoreStrings.Xbox_HistoryRefused);
            case HttpStatusCode.TooManyRequests:
                throw new XboxLiveException(CoreStrings.Xbox_RateLimited);
            default:
                throw new XboxLiveException(CoreText.Format(CoreStrings.Xbox_HttpError, (int)response.StatusCode, response.ReasonPhrase));
        }
    }
}
