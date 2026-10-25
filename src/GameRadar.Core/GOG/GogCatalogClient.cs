using System.Text.Json;
using System.Text.Json.Serialization;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;

namespace GameRadar.Core.GOG;

/// <summary>
/// Candidate games from the GOG store: the best sellers, with genres, tags,
/// reviews and pictures (catalog.gog.com, public, no account nor key).
/// Genres and tags are translated into the Microsoft Store genres, the
/// vocabulary of the taste profile.
/// </summary>
public sealed class GogCatalogClient : IStoreCatalogClient
{
    // English titles, as every source; prices of the French store (not shown).
    private const string CatalogUrl = "https://catalog.gog.com/v1/catalog?limit=100&order=desc:bestselling&productType=in:game&countryCode=FR&locale=en-US&currencyCode=EUR&page=";

    /// <summary>Pages of 100 games (the most the API gives): the 1,000 best sellers.</summary>
    public const int PageCount = 10;

    /// <summary>Fewer reviews: too little known to recommend.</summary>
    public const int MinReviewCount = 100;

    /// <summary>GOG rates out of 50: 35 = 70 % positive, the bar of the Steam catalog.</summary>
    public const int MinRating = 35;

    // GOG community tags of adult content. Left out: the app is shared.
    private static readonly string[] AdultTags = ["Nudity", "Sexual Content", "Hentai", "NSFW"];

    // Only the first tags describe the game; the tail is noise.
    private const int TagsConsidered = 8;
    private const int MaxGenres = 3;

    private readonly HttpClient _http;

    public GogCatalogClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>Fetches the best sellers, filtered and translated (<see cref="PageCount"/> requests).</summary>
    public async Task<IReadOnlyList<CatalogGame>> FetchCatalogAsync(CancellationToken ct = default)
    {
        var games = new List<CatalogGame>();
        for (int page = 1; page <= PageCount; page++)
        {
            using var response = await _http.GetAsync(CatalogUrl + page, ct).ConfigureAwait(false);
            if (HttpOutcomes.Classify(response) != HttpOutcome.Ok)
                throw new GogException(CoreStrings.Gog_CatalogDown);
            games.AddRange(ParseJson(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)));
        }
        // A game may move from one page to the next between two requests.
        return games.DistinctBy(g => g.ProductId).ToList();
    }

    /// <summary>One page of the catalog → candidates, minus packs, the little known, the poorly rated, the adult and the untranslatable.</summary>
    internal static IReadOnlyList<CatalogGame> ParseJson(string json)
    {
        CatalogPageDto? page;
        try
        {
            page = JsonSerializer.Deserialize(json, GogJsonContext.Default.CatalogPageDto);
        }
        catch (JsonException ex)
        {
            throw new GogException(CoreStrings.Gog_CatalogFormat, ex);
        }
        if (page?.Products is not { } products)
            throw new GogException(CoreStrings.Gog_CatalogFormat);

        var games = new List<CatalogGame>();
        foreach (var product in products)
        {
            if (product is not { ProductType: "game", Id: { Length: > 0 } id, Title: { Length: > 0 } title, StoreLink: { } link, ReviewsRating: int rating, ReviewsCount: int reviews }
                || reviews < MinReviewCount
                || rating < MinRating)
                continue;

            var tags = (product.Tags ?? []).Select(t => t.Name).OfType<string>().ToList();
            if (tags.Intersect(AdultTags).Any())
                continue;

            // GOG's own genres first (curated, main one first), then the tags.
            var genres = (product.Genres ?? []).Select(g => g.Name)
                .Concat(tags.Take(TagsConsidered))
                .Select(name => name is null ? null : GenreOf(name))
                .OfType<string>()
                .Distinct()
                .Take(MaxGenres)
                .ToList();
            if (genres.Count == 0)
                continue;

            var modes = (product.Features ?? []).Any(f => f.Slug == "single") ? PlayModes.Solo : PlayModes.None;
            // "multi" and "coop" say neither online nor local: only the tags tell local play.
            if (tags.Any(t => t is "Local Co-Op" or "Local Multiplayer" or "Split Screen"))
                modes |= PlayModes.Local;

            games.Add(new CatalogGame(
                ProductId: "gog:" + id,
                Title: title,
                XboxTitleId: null,
                PackageFamilyName: null,
                Genres: genres,
                OnPc: false,
                OnConsole: false,
                LeavingSoon: false,
                // Same scale as the other catalogs: 45 / 50 = 4.5 / 5.
                AverageRating: rating / 10.0,
                RatingCount: reviews,
                Modes: modes,
                ImageUrl: ImageUrlOf(product.CoverHorizontal),
                GogStoreLink: link));
        }
        return games;
    }

    // The cover is a PNG of ~2 MB (1600 × 740). GOG's image server gives other
    // sizes by name: "_product_card_v2_mobile_slider_639.jpg" is 639 × 361, ~50 KB,
    // about the card's size (560 × 315).
    internal static string? ImageUrlOf(string? cover) =>
        cover is null ? null : Path.ChangeExtension(cover, null) + "_product_card_v2_mobile_slider_639.jpg";

    /// <summary>
    /// A GOG genre or tag → Microsoft Store genre, or null when it says
    /// nothing of the gameplay ("Fantasy", "Turn-based", "Sci-fi"...). GOG's
    /// tags are mostly Steam's: what is not a GOG word goes through Steam's table.
    /// </summary>
    internal static string? GenreOf(string name) => name switch
    {
        "Role-playing" => "Role playing",
        "Point-and-click" or "Point&Click" => "Action & adventure",
        "Managerial" or "Economic" or "Building" or "Virtual life" => "Simulation",
        "Rally" or "Off-road" => "Racing & flying",
        "Soccer" => "Sports",
        _ => SteamCatalogClient.GenreOfTag(name)
    };

    // ---------- DTOs: catalog page ----------

    internal sealed class CatalogPageDto
    {
        [JsonPropertyName("products")]
        public List<ProductDto>? Products { get; set; }
    }

    internal sealed class ProductDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        // "game", "pack" (a bundle), "dlc".
        [JsonPropertyName("productType")]
        public string? ProductType { get; set; }

        [JsonPropertyName("genres")]
        public List<NameDto>? Genres { get; set; }

        [JsonPropertyName("tags")]
        public List<NameDto>? Tags { get; set; }

        [JsonPropertyName("features")]
        public List<NameDto>? Features { get; set; }

        // Out of 50.
        [JsonPropertyName("reviewsRating")]
        public int? ReviewsRating { get; set; }

        [JsonPropertyName("reviewsCount")]
        public int? ReviewsCount { get; set; }

        [JsonPropertyName("coverHorizontal")]
        public string? CoverHorizontal { get; set; }

        [JsonPropertyName("storeLink")]
        public string? StoreLink { get; set; }
    }

    internal sealed class NameDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }
    }
}
