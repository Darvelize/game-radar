using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GameRadar.Core.Models;
using GameRadar.Core.Resources;
using GameRadar.Core.Services;

namespace GameRadar.Core.PlayStation;

/// <summary>
/// Connector for the PS Plus catalogs (Extra and Premium), through the public
/// lists of the "All PlayStation Plus games" page of playstation.com. No
/// account, no key: the NPSSO and the tokens are never sent here.
/// </summary>
public sealed class PsPlusCatalogClient
{
    private const string ListsUrl = "https://www.playstation.com/bin/imagic/gameslist";

    /// <summary>
    /// The lists and the plans they belong to, as the page counts them: the Game
    /// Catalog and the Ubisoft+ Classics are Extra (so Premium too), the Classics
    /// Catalog is Premium only (measured on the French lists: Extra ⊂ Premium).
    /// </summary>
    private static readonly (string List, PsPlusTier Tiers)[] Lists =
    [
        ("plus-games-list", PsPlusTier.Extra | PsPlusTier.Premium),
        ("ubisoft-classics-list", PsPlusTier.Extra | PsPlusTier.Premium),
        ("plus-classics-list", PsPlusTier.Premium)
    ];

    // PS Plus Essential's games of the month (no catalog: the "Jeux gratuits" page).
    private const string MonthlyList = "plus-monthly-games-list";


    private readonly HttpClient _http;
    private readonly string _locale;

    /// <param name="http">Shared HttpClient.</param>
    /// <param name="locale">Page locale, e.g. "fr-fr" (the catalog differs per country; "en-fr" does not exist and answers 404).</param>
    public PsPlusCatalogClient(HttpClient http, string locale = "fr-fr")
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _locale = locale;
    }

    /// <summary>Fetches the whole catalog, every plan: one request per list (3), no pagination.</summary>
    public async Task<IReadOnlyList<CatalogGame>> FetchCatalogAsync(CancellationToken ct = default)
    {
        // Concept id -> game: a game in several lists (Ubisoft+ Classics are also in the
        // Game Catalog) or twice in one list (one entry per console version) counts once,
        // with every plan that includes it.
        var byProduct = new Dictionary<string, CatalogGame>();
        foreach (var (list, tiers) in Lists)
        {
            string json = await GetStringAsync(list, ct).ConfigureAwait(false);
            foreach (var game in ParseJson(json, tiers))
                byProduct[game.ProductId] = byProduct.TryGetValue(game.ProductId, out var known)
                    ? known with { PsPlusTiers = known.PsPlusTiers | game.PsPlusTiers }
                    : game;
        }

        return byProduct.Values.ToList();
    }

    /// <summary>The games of one list, all in the plans <paramref name="tiers"/>.</summary>
    /// <exception cref="PlayStationException">The answer is not a list of blocks of games.</exception>
    internal static IReadOnlyList<CatalogGame> ParseJson(string json, PsPlusTier tiers) =>
        ReadGames(json)
            .Select(g => new CatalogGame(
                // Like "steam:<appid>" for a Steam game: unique among the candidates.
                ProductId: $"psplus:{g.ConceptId}",
                Title: g.Title!.Trim(),
                XboxTitleId: null,
                PackageFamilyName: null,
                Genres: (g.Genre ?? []).Select(PlayStationClient.GenreOf).OfType<string>().Distinct()
                    .Take(PlayStationClient.MaxGenres).ToList(),
                // Not a Game Pass game: no platform, nothing leaving.
                OnPc: false,
                OnConsole: false,
                LeavingSoon: false,
                ImageUrl: g.ImageUrl,
                PsPlusTiers: tiers,
                PlayStationConceptId: g.ConceptId.ToString()))
            .ToList();

    /// <summary>The PS Plus Essential games of the month (one request), for the "Jeux gratuits" page.</summary>
    public async Task<IReadOnlyList<FreeGame>> FetchMonthlyGamesAsync(CancellationToken ct = default) =>
        ParseMonthlyJson(await GetStringAsync(MonthlyList, ct).ConfigureAwait(false));

    /// <summary>
    /// The games of the month, one per concept: a game's PS Plus starter pack shares
    /// its concept and came out later, so the oldest entry is the game. No end date
    /// in the list (usually the first Tuesday of the next month): none is guessed.
    /// </summary>
    /// <exception cref="PlayStationException">The answer is not a list of blocks of games.</exception>
    internal static IReadOnlyList<FreeGame> ParseMonthlyJson(string json) =>
        ReadGames(json)
            .GroupBy(g => g.ConceptId)
            .Select(concept => concept.OrderBy(g => g.ReleaseDate ?? DateTimeOffset.MaxValue).First())
            .Select(g => new FreeGame(
                GameTitle.Clean(g.Title!),
                // Without language: the page adds the app's, like the cards of "Pour toi".
                $"https://store.playstation.com/concept/{g.ConceptId}",
                ImageUrl: g.ImageUrl))
            .ToList();

    // The games of a list (an entry with a concept and a title), its letter blocks flattened.
    /// <exception cref="PlayStationException">The answer is not a list of blocks of games.</exception>
    private static IEnumerable<PsPlusGameDto> ReadGames(string json)
    {
        List<PsPlusBlockDto>? blocks;
        try
        {
            blocks = JsonSerializer.Deserialize(json, PlayStationJsonContext.Default.ListPsPlusBlockDto);
        }
        catch (JsonException ex)
        {
            throw new PlayStationException(CoreStrings.PlayStation_Format, inner: ex);
        }
        if (blocks is null)
            throw new PlayStationException(CoreStrings.PlayStation_Format);

        return blocks.SelectMany(b => b.Games ?? []).Where(g => g.ConceptId is not null && !string.IsNullOrWhiteSpace(g.Title));
    }

    private async Task<string> GetStringAsync(string list, CancellationToken ct)
    {
        string url = $"{ListsUrl}?locale={_locale}&categoryList={list}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw PlayStationException.RateLimited();
        if (!response.IsSuccessStatusCode)
            throw new PlayStationException(
                CoreText.Format(CoreStrings.PlayStation_HttpError, (int)response.StatusCode, response.ReasonPhrase));

        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
