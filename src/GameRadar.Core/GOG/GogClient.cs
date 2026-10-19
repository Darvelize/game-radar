using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GameRadar.Core.Services;

namespace GameRadar.Core.GOG;

/// <summary>
/// The games of a public GOG profile: the whole library, and for the games
/// GOG Galaxy followed, play time, last session and achievement percentage
/// (the "Games" page of the profile, undocumented, no sign-in: the profile
/// must be public).
/// </summary>
public sealed partial class GogClient
{
    /// <summary>The platform name of GOG games and candidates (a brand, never translated).</summary>
    public const string PlatformName = "GOG";

    private readonly HttpClient _http;

    public GogClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// One page (50 games) of the profile as raw JSON, read with <see cref="ParseJson"/>.
    /// Throws <see cref="GogException"/> with an explanation: profile not
    /// public, no such profile, rate limit, GOG down.
    /// </summary>
    public async Task<string> FetchPageJsonAsync(string user, int page, CancellationToken ct = default)
    {
        string url = $"https://www.gog.com/u/{Uri.EscapeDataString(user)}/games/stats?sort=recent_playtime&order=desc&page={page}";
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        switch (HttpOutcomes.Classify(response))
        {
            case HttpOutcome.Ok:
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // An existing profile its owner did not make public.
            case HttpOutcome.Forbidden or HttpOutcome.Unauthorized:
                throw new GogException(CoreText.Format(CoreStrings.Gog_PrivateProfile, user));
            case HttpOutcome.NotFound:
                throw new GogException(CoreText.Format(CoreStrings.Gog_NoSuchProfile, user));
            case HttpOutcome.RateLimited:
                throw new GogException(CoreStrings.Gog_RateLimited);
            default:
                throw new GogException(CoreStrings.Gog_ProfileDown);
        }
    }

    /// <summary>
    /// One page of the profile → its games and the page count. Throws
    /// <see cref="GogException"/> on an unexpected answer (a maintenance page...).
    /// </summary>
    internal static GogProfilePage ParseJson(string json)
    {
        ProfilePageDto? page;
        try
        {
            page = JsonSerializer.Deserialize(json, GogJsonContext.Default.ProfilePageDto);
        }
        catch (JsonException ex)
        {
            throw new GogException(CoreStrings.Gog_ProfileFormat, ex);
        }
        if (page?.Embedded?.Items is not { } items)
            throw new GogException(CoreStrings.Gog_ProfileFormat);

        var games = new List<GogLibraryGame>();
        foreach (var item in items)
        {
            if (item.Game is not { Id: { Length: > 0 } id, Title: { Length: > 0 } title })
                continue;

            // "stats" is an object keyed by the user id, or [] for a game GOG
            // Galaxy never followed (owned, maybe played outside Galaxy).
            JsonElement? stats = item.Stats is { ValueKind: JsonValueKind.Object } byUser
                && byUser.EnumerateObject().FirstOrDefault().Value is { ValueKind: JsonValueKind.Object } mine
                ? mine : null;
            games.Add(new GogLibraryGame(
                id,
                title.Trim(),
                stats is { } s && s.TryGetProperty("playtime", out var playtime) && playtime.ValueKind == JsonValueKind.Number && playtime.TryGetInt32(out int minutes) ? minutes : null,
                stats is { } t && t.TryGetProperty("lastSession", out var session) && session.ValueKind == JsonValueKind.String && session.TryGetDateTimeOffset(out var last) ? last : null,
                stats is { } u && u.TryGetProperty("achievementsPercentage", out var achievements) && achievements.ValueKind == JsonValueKind.Number && achievements.TryGetDouble(out double percentage) ? percentage : null,
                // A PNG of the store ("//images.gog-statics.com/…" in the samples): at the card's size, in https.
                GogCatalogClient.ImageUrlOf(item.Game.Image is { } image && image.StartsWith("//", StringComparison.Ordinal) ? "https:" + image : item.Game.Image)));
        }
        return new GogProfilePage(Math.Max(page.Pages ?? 1, 1), games);
    }

    /// <summary>
    /// The profile name in what the user pasted: the name itself, or the
    /// address of their profile page ("gog.com/u/name", a sub-page, a
    /// language prefix, a query). Null when it is neither.
    /// </summary>
    internal static string? UserOf(string pasted)
    {
        string text = pasted.Trim();
        if (ProfileUrlPattern().Match(text) is { Success: true } url)
            return url.Groups[1].Value;
        return BareNamePattern().IsMatch(text) ? text : null;
    }

    /// <summary><see cref="UserOf"/>, or a <see cref="GogException"/> that says what to paste.</summary>
    public static string RequireUser(string pasted) =>
        UserOf(pasted) ?? throw new GogException(CoreStrings.Gog_PasteProfile);

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?gog\.com/(?:[a-z]{2}(?:-[a-z]{2})?/)?u/([A-Za-z0-9_.-]+)(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileUrlPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+$")]
    private static partial Regex BareNamePattern();

    // ---------- DTOs: profile page ----------

    internal sealed class ProfilePageDto
    {
        [JsonPropertyName("pages")]
        public int? Pages { get; set; }

        [JsonPropertyName("_embedded")]
        public EmbeddedDto? Embedded { get; set; }
    }

    internal sealed class EmbeddedDto
    {
        [JsonPropertyName("items")]
        public List<ItemDto>? Items { get; set; }
    }

    internal sealed class ItemDto
    {
        [JsonPropertyName("game")]
        public GameDto? Game { get; set; }

        // An object keyed by the user id, or [] (see ParseJson): read by hand.
        [JsonPropertyName("stats")]
        public JsonElement? Stats { get; set; }
    }

    internal sealed class GameDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("image")]
        public string? Image { get; set; }
    }
}

/// <summary>One page of a GOG profile: its games, and how many pages there are.</summary>
internal sealed record GogProfilePage(int Pages, IReadOnlyList<GogLibraryGame> Games);

/// <summary>
/// A game of the GOG library. Play time, last session and achievement
/// percentage are null for a game GOG Galaxy never followed.
/// </summary>
internal sealed record GogLibraryGame(string Id, string Title, int? PlaytimeMinutes, DateTimeOffset? LastSession, double? AchievementsPercentage, string? ImageUrl = null);
