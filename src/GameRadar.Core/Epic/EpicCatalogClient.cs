using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;

namespace GameRadar.Core.Epic;

/// <summary>
/// Candidate games from the Epic store: its top sellers, most played and top
/// rated collections, with the players' rating (the store's GraphQL, no
/// account: only the launcher's identity, without which it answers 403). Genre tags are
/// translated into the Microsoft Store genres, the vocabulary of the taste profile.
/// </summary>
public sealed class EpicCatalogClient : IStoreCatalogClient
{
    /// <summary>The players' rating out of 5: below 3, not recommended (user's choice, wider than the 70 % of Steam and GOG).</summary>
    public const double MinRating = 3.0;

    private const int MaxGenres = 3;

    // The store page of a game, without language: the card adds the app's
    // (like the other stores' links), so the cache does not freeze one.
    internal const string StorePage = "https://store.epicgames.com/p/";

    /// <summary>
    /// Epic gives no number of ratings; the games of its collections are well
    /// known. Weighed like a game with this many reviews, so that the rating
    /// counts in the ranking: with none, the engine's smoothing brings any
    /// rating back to its prior.
    /// </summary>
    internal const int RatingWeight = 1000;


    /// <summary>The store's lists the candidates come from (227 games together, measured): no "best sellers" sort exists otherwise.</summary>
    internal static readonly string[] Collections = ["top-sellers", "most-played", "top-player-reviewed"];

    /// <summary>One rating per candidate (~230 requests a week): this many at once, Epic's limit being unknown.</summary>
    internal const int MaxRatingsAtOnce = 8;

    private const string CollectionQuery = "query collectionLayoutQuery($locale: String, $slug: String) { Storefront { collectionLayout(locale: $locale, slug: $slug) { collectionOffers { title namespace categories { path } tags { name groupName } keyImages { type url } catalogNs { mappings(pageType: \"productHome\") { pageSlug } } } } } }";
    private const string RatingQuery = "query getProductResult($sandboxId: String!, $locale: String!) { RatingsPolls { getProductResult(sandboxId: $sandboxId, locale: $locale) { averageRating } } }";

    private readonly HttpClient _http;

    public EpicCatalogClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// The games of the three collections, once each, with their rating
    /// (<see cref="MaxRatingsAtOnce"/> at a time), filtered and translated.
    /// </summary>
    public async Task<IReadOnlyList<CatalogGame>> FetchCatalogAsync(CancellationToken ct = default)
    {
        // The three at once (independent); read in their order.
        var collections = await Task.WhenAll(Collections.Select(async slug => ParseCollection(await PostAsync(
            new GraphQlRequest<CollectionVariables> { Query = CollectionQuery, Variables = new() { Slug = slug } },
            EpicJsonContext.Default.GraphQlRequestCollectionVariables, ct).ConfigureAwait(false)))).ConfigureAwait(false);
        // A game in several collections: the first one keeps it.
        var unique = collections.SelectMany(c => c).DistinctBy(o => o.Namespace).ToList();

        // Not one rating among well-known games (206 of 218, measured): the rating
        // query broke, the old catalog stays.
        var ratings = await StoreLookup.FetchAllAsync(unique, MaxRatingsAtOnce,
            (offer, token) => FetchRatingAsync(offer.Namespace, token),
            () => new EpicException(CoreStrings.Epic_Format), ct).ConfigureAwait(false);

        return unique.Select((offer, i) => ToCatalogGame(offer, ratings[i])).OfType<CatalogGame>().ToList();
    }

    /// <summary>A game's rating, null when Epic has none.</summary>
    private async Task<double?> FetchRatingAsync(string @namespace, CancellationToken ct) =>
        ParseRating(await PostAsync(
            new GraphQlRequest<SandboxVariables> { Query = RatingQuery, Variables = new() { SandboxId = @namespace } },
            EpicJsonContext.Default.GraphQlRequestSandboxVariables, ct).ConfigureAwait(false));

    /// <summary>A store GraphQL request, as the launcher and without any account.</summary>
    private async Task<string> PostAsync<T>(T body, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, EpicClient.GraphQlUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, typeInfo), Encoding.UTF8, "application/json")
        };
        request.Headers.UserAgent.ParseAdd(EpicClient.StoreUserAgent);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        return HttpOutcomes.Classify(response) switch
        {
            HttpOutcome.Ok => await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false),
            HttpOutcome.RateLimited => throw EpicException.RateLimited(),
            // 403: the launcher's identity no longer accepted; 5xx: down.
            _ => throw new EpicException(CoreStrings.Epic_StoreDown)
        };
    }

    /// <summary>
    /// A store collection's games, their genre tags translated into the Microsoft
    /// Store genres (3 at most). Left out before any rating is asked: an add-on,
    /// an offer without a store page, and one with no genre the profile knows
    /// (an app has no genre tag at all). A collection Epic no longer has gives
    /// none (the two others still count).
    /// </summary>
    internal static IReadOnlyList<EpicOffer> ParseCollection(string json)
    {
        var offers = EpicClient.Data(json, EpicJsonContext.Default.GraphQlEnvelopeCollectionData)
            .Storefront?.CollectionLayout?.CollectionOffers ?? [];
        var result = new List<EpicOffer>();
        foreach (var offer in offers)
        {
            if (offer is not { Namespace: { Length: > 0 } ns, Title: { Length: > 0 } title }
                || offer.CatalogNs?.Mappings?.FirstOrDefault()?.PageSlug is not { Length: > 0 } pageSlug)
                continue;
            var paths = (offer.Categories ?? []).Select(c => c.Path).OfType<string>().ToList();
            var tags = offer.Tags ?? [];
            var genres = TagsOf(tags, "genre").Select(SteamCatalogClient.GenreOfTag).OfType<string>().Distinct().Take(MaxGenres).ToList();
            if (!paths.Contains("games") || paths.Contains("addons") || genres.Count == 0)
                continue;

            result.Add(new EpicOffer(ns, title.Trim(), genres, TagsOf(tags, "feature"), pageSlug,
                offer.KeyImages?.FirstOrDefault(i => i.Type == "OfferImageWide")?.Url));
        }
        return result;
    }

    private static List<string> TagsOf(List<TagDto> tags, string group) =>
        tags.Where(t => t.GroupName == group).Select(t => t.Name).OfType<string>().ToList();

    /// <summary>
    /// The players' rating out of 5, or null when Epic has none: a null rating,
    /// or an error inside a 200 (a little known game answers 404 there, measured).
    /// </summary>
    internal static double? ParseRating(string json) =>
        EpicClient.Read(json, EpicJsonContext.Default.GraphQlEnvelopeRatingData)?.Data?.RatingsPolls?.ProductResult?.AverageRating;

    /// <summary>
    /// The offer as a candidate, or null when rated below <see cref="MinRating"/>
    /// (a game without rating is kept: the collections already vouch for it).
    /// </summary>
    internal static CatalogGame? ToCatalogGame(EpicOffer offer, double? rating)
    {
        if (rating < MinRating)
            return null;

        return new CatalogGame(
            ProductId: "epic:" + offer.Namespace,
            Title: offer.Title,
            XboxTitleId: null,
            PackageFamilyName: null,
            Genres: offer.Genres,
            OnPc: false,
            OnConsole: false,
            LeavingSoon: false,
            AverageRating: rating,
            RatingCount: rating is null ? 0 : RatingWeight,
            Modes: offer.Features.Aggregate(PlayModes.None, (all, feature) => all | ModeOf(feature)),
            ImageUrl: offer.ImageUrl,
            EpicStoreLink: StorePage + Uri.EscapeDataString(offer.PageSlug));
    }

    // Epic's feature tags (measured names); vague like Steam's: "Co-op" and "Multiplayer"
    // say neither online nor local, so no mode (Steam's "Co-op" category neither).
    internal static PlayModes ModeOf(string feature) => feature switch
    {
        "Single Player" => PlayModes.Solo,
        "Online Co-op" => PlayModes.CoopOnline,
        "Online Multiplayer" or "MMO" or "Competitive" => PlayModes.MultiOnline,
        "Local Multiplayer" or "Local Co-op" or "Split Screen" => PlayModes.Local,
        _ => PlayModes.None
    };
}

/// <summary>A game of a store collection, before its rating is known (genres already translated).</summary>
internal sealed record EpicOffer(
    string Namespace, string Title, IReadOnlyList<string> Genres, IReadOnlyList<string> Features, string PageSlug, string? ImageUrl);
