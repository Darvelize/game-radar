using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.Amazon;

/// <summary>
/// The games Amazon Prime gives away (Prime Gaming became part of Luna in 2025),
/// read as the public claims page reads them, without any account: the page gives
/// an anonymous session cookie and a form token, then its GraphQL lists the games.
/// The site's own client id is sent: a deliberate exception, there being no public API.
/// </summary>
public sealed partial class AmazonFreeGamesClient
{
    private const string PageUrl = "https://luna.amazon.com/claims/home";
    private const string GraphQlUrl = "https://luna.amazon.com/graphql";

    // The id the site's own scripts send (main.js), without which the API answers 403.
    private const string ClientId = "CarboniteApp";

    private const string Query =
        "query { items(collectionType: FREE_GAMES) { items { category assets { title externalClaimLink cardMedia { defaultMedia { src1x } } } offers { startTime endTime } } } }";

    // Where a game is redeemed, from the end of its claim page's slug ("doom-gog").
    private static readonly Dictionary<string, string> Stores = new()
    {
        ["gog"] = "GOG",
        ["epic"] = "Epic Games",
        ["aga"] = "Amazon Games",
        ["microsoft"] = "Microsoft Store",
        ["legacy"] = "Legacy Games",
    };

    private readonly HttpClient _http;

    public AmazonFreeGamesClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>The games free at <paramref name="now"/>, ending first: 2 requests.</summary>
    public async Task<IReadOnlyList<FreeGame>> FetchAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        string token;
        string cookie;
        using (var pageRequest = new HttpRequestMessage(HttpMethod.Get, PageUrl))
        {
            pageRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var page = await SendAsync(pageRequest, ct).ConfigureAwait(false);
            token = ReadToken(await page.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            // Sent by hand, whatever the shared client does with cookies (measured: both work).
            cookie = string.Join("; ", (page.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [])
                .Select(c => c.Split(';')[0]));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new GraphQlRequest { Query = Query }, AmazonJsonContext.Default.GraphQlRequest),
                Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("csrf-token", token);
        request.Headers.Add("Client-Id", ClientId);
        // English titles, like everywhere in the app.
        request.Headers.Add("prime-gaming-language", "en-US");
        if (cookie.Length > 0)
            request.Headers.Add("Cookie", cookie);

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        return ParseJson(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), now);
    }

    // The answer, only when it is a success (the caller disposes it); an error otherwise.
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var outcome = HttpOutcomes.Classify(response);
        if (outcome == HttpOutcome.Ok)
            return response;
        response.Dispose();
        throw outcome switch
        {
            HttpOutcome.RateLimited => AmazonException.RateLimited(),
            // The token, the cookie or the client id no longer accepted: the site changed.
            HttpOutcome.Forbidden => new AmazonException(CoreStrings.Amazon_Refused),
            _ => new AmazonException(CoreStrings.Amazon_Down)
        };
    }

    /// <summary>The form token of the claims page, sent back with the query.</summary>
    /// <exception cref="AmazonException">The page has none (it changed, or is an error page).</exception>
    internal static string ReadToken(string html) =>
        TokenInput().Match(html) is { Success: true } match ? match.Groups[1].Value : throw new AmazonException(CoreStrings.Amazon_Format);

    /// <summary>
    /// The full games free at <paramref name="now"/> (not in-game loot), with a claim
    /// page on Amazon only, ending first.
    /// </summary>
    /// <exception cref="AmazonException">The answer has no list of games.</exception>
    internal static IReadOnlyList<FreeGame> ParseJson(string json, DateTimeOffset now)
    {
        List<ItemDto>? items;
        try
        {
            items = JsonSerializer.Deserialize(json, AmazonJsonContext.Default.GraphQlEnvelope)?.Data?.Items?.Items;
        }
        catch (JsonException ex)
        {
            throw new AmazonException(CoreStrings.Amazon_Format, ex);
        }
        if (items is null)
            throw new AmazonException(CoreStrings.Amazon_Format);

        var games = new List<FreeGame>();
        foreach (var item in items)
        {
            if (item is not { Category: "FULL_GAME", Assets: { Title: { Length: > 0 } title, ExternalClaimLink: { } link } assets }
                || !Uri.TryCreate(link, UriKind.Absolute, out var claim) || claim.Scheme != Uri.UriSchemeHttps
                || claim.Host is not ("gaming.amazon.com" or "luna.amazon.com"))
                continue;
            var offer = (item.Offers ?? []).FirstOrDefault(o => o.StartTime <= now && now < o.EndTime);
            if (offer is null)
                continue;

            string slug = claim.Segments.Length > 1 ? claim.Segments[1].TrimEnd('/') : "";
            string? store = Stores.GetValueOrDefault(slug[(slug.LastIndexOf('-') + 1)..]);
            games.Add(new FreeGame(title.Trim(), link, offer.EndTime, OriginalPicture(assets.CardMedia?.DefaultMedia?.Src1x), store));
        }
        return games.OrderBy(g => g.Until).ToList();
    }

    // "…/I/71iiIFosTLL._SX256_._AGmerlin_._FMwebp_.jpg" → "…/I/71iiIFosTLL.jpg": the
    // original JPEG (95 KB, measured) rather than a 256 px WebP, too small for a card.
    private static string? OriginalPicture(string? url) => url is null ? null : ImageModifiers().Replace(url, ".");

    [GeneratedRegex("""name=['"]csrf-key['"]\s+value=['"]([^'"]+)['"]""")]
    private static partial Regex TokenInput();

    [GeneratedRegex(@"\._[^/]*_\.")]
    private static partial Regex ImageModifiers();
}
