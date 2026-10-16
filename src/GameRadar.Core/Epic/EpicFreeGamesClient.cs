using System.Net.Http.Headers;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.Epic;

/// <summary>
/// Epic's games of the week, through the public promotions list of the store
/// (no account, no launcher identity): the "Jeux gratuits" page.
/// </summary>
public sealed class EpicFreeGamesClient
{
    // French market, English titles (the titles of the whole app).
    private const string PromotionsUrl =
        "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=en-US&country=FR&allowCountries=FR";

    private readonly HttpClient _http;

    public EpicFreeGamesClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>The games free at <paramref name="now"/>, ending first.</summary>
    public async Task<IReadOnlyList<FreeGame>> FetchAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PromotionsUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        return HttpOutcomes.Classify(response) switch
        {
            HttpOutcome.Ok => ParseJson(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), now),
            HttpOutcome.RateLimited => throw EpicException.RateLimited(),
            _ => throw new EpicException(CoreStrings.Epic_Down)
        };
    }

    /// <summary>
    /// The games free at <paramref name="now"/>: a current offer that leaves nothing
    /// to pay. Left out: sales, next week's games, add-ons, a game without a page.
    /// </summary>
    /// <exception cref="EpicException">The answer has no list of games.</exception>
    internal static IReadOnlyList<FreeGame> ParseJson(string json, DateTimeOffset now)
    {
        // Errors next to the data are read past: this list is known to report
        // some for a game's details while the list itself is whole.
        var elements = EpicClient.Read(json, EpicJsonContext.Default.GraphQlEnvelopePromotionsData)?.Data?.Catalog?.SearchStore?.Elements
            ?? throw new EpicException(CoreStrings.Epic_Format);

        var games = new List<FreeGame>();
        foreach (var element in elements)
        {
            if (element is not { Title: { Length: > 0 } title, Price.TotalPrice.DiscountPrice: 0 } || element.OfferType == "ADD_ON")
                continue;
            var offer = (element.Promotions?.PromotionalOffers ?? []).SelectMany(g => g.PromotionalOffers ?? [])
                .FirstOrDefault(o => o.DiscountSetting?.DiscountPercentage == 0 && o.StartDate <= now && now < o.EndDate);
            // The page: the catalog's mapping, else the offer's, else the product slug
            // without its sub-page ("thems-fightin-herds/home", measured).
            string? slug = (element.CatalogNs?.Mappings ?? []).Concat(element.OfferMappings ?? [])
                .Select(m => m.PageSlug).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                ?? element.ProductSlug?.Split('/')[0];
            if (offer is null || string.IsNullOrEmpty(slug))
                continue;

            var images = element.KeyImages ?? [];
            string? image = (images.FirstOrDefault(i => i.Type == "OfferImageWide") ?? images.FirstOrDefault(i => i.Type == "Thumbnail"))?.Url;
            games.Add(new FreeGame(title.Trim(), EpicCatalogClient.StorePage + Uri.EscapeDataString(slug), offer.EndDate, image));
        }
        return games.OrderBy(g => g.Until).ToList();
    }
}
