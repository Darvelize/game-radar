using System.Net.Http.Headers;
using System.Text.Json;
using GameRadar.Core.Models;

namespace GameRadar.Core.Updates;

/// <summary>A newer GameRadar release, and where to download it.</summary>
public sealed record AvailableUpdate(Version Version, Uri DownloadPage);

/// <summary>
/// Asks GitHub for the latest public release of GameRadar (1 request, no key,
/// no personal data sent) and tells whether it is newer than the running one.
/// </summary>
public sealed class UpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Darvelize/game-radar/releases/latest";

    /// <summary>Public page of the latest release, opened in the browser to download it.</summary>
    public static readonly Uri DownloadPage = new("https://github.com/Darvelize/game-radar/releases/latest");


    private readonly HttpClient _http;

    public UpdateChecker(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// The latest release when it is newer than <paramref name="current"/>,
    /// otherwise null. Never throws for network or format reasons (offline,
    /// GitHub limit of 60 requests per hour, no release yet...): an update
    /// check must never get in the user's way. Only a cancellation asked by
    /// the caller goes through.
    /// </summary>
    public async Task<AvailableUpdate?> CheckAsync(Version current, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            // GitHub refuses any request without a User-Agent (403).
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GameRadar", current.ToString()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var latest = ParseLatestVersion(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return latest is not null && IsNewer(latest, current) ? new AvailableUpdate(latest, DownloadPage) : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        // HttpClient's own timeout also arrives as an OperationCanceledException,
        // but without the caller's token cancelled: it is swallowed like the rest.
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Version of the release tag ("v1.2.0"), or null when the answer or the tag is unexpected.</summary>
    internal static Version? ParseLatestVersion(string json)
    {
        string? tag = JsonSerializer.Deserialize(json, CoreJsonContext.Default.GitHubReleaseDto)?.TagName;
        return tag is null ? null : ParseTag(tag);
    }

    /// <summary>"v1.2.0" or "1.2.0" → 1.2.0; a pre-release tag ("v1.2.0-beta") → null.</summary>
    internal static Version? ParseTag(string tag)
    {
        string number = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(number, out var version) ? version : null;
    }

    /// <summary>
    /// Compares versions with their missing parts counted as 0. A plain
    /// comparison would say 1.0.0 &lt; 1.0.0.0: the assembly version always
    /// has 4 parts, a tag usually 3, and a missing part is -1 for Version.
    /// </summary>
    public static bool IsNewer(Version candidate, Version current) =>
        Complete(candidate) > Complete(current);

    private static Version Complete(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
