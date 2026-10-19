using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GameRadar.Core.Models;
using GameRadar.Core.Services;

namespace GameRadar.Core.GamePass;

/// <summary>
/// Xbox's Free Play Days (games free to play from Thursday to Sunday with a Game
/// Pass plan, not to keep), through the API xbox.com's own page reads, without
/// account: the "Jeux gratuits" page.
/// </summary>
public sealed class XboxFreePlayDaysClient
{
    // English titles (the titles of the whole app): the same list as in French.
    private const string BrowseUrl = "https://emerald.xboxservices.com/xboxcomfd/browse?locale=en-US";
    private const string ChannelId = "FREE-PLAY-DAYS";
    private const string ChannelKey = "BROWSE_CHANNELID=" + ChannelId + "_FILTERS=";

    /// <summary>A game's store page, without language (the app adds its own).</summary>
    internal const string StorePage = "https://www.xbox.com/games/store/-/";

    // The Free Play Days end on Sunday night, Seattle time (the Windows id, else the IANA one).
    private static readonly TimeZoneInfo Pacific =
        TimeZoneInfo.TryFindSystemTimeZoneById("Pacific Standard Time", out var pacific) ? pacific : TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    private readonly HttpClient _http;

    public XboxFreePlayDaysClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>
    /// This week's games, free until Sunday. The list has no end date and stays
    /// online once over: the same games as <paramref name="previous"/>, whose end
    /// passed, are still last week's (Thursday before xbox.com's update).
    /// </summary>
    public async Task<IReadOnlyList<FreeGame>> FetchAsync(DateTimeOffset now, FreeGameList? previous, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BrowseUrl)
        {
            Content = JsonContent.Create(
                new BrowseRequestDto { ChannelId = ChannelId, ChannelKeyToBeUsedInResponse = ChannelKey },
                GamePassJsonContext.Default.BrowseRequestDto),
        };
        request.Headers.Add("x-ms-api-version", "1.1");
        // A correlation vector: any value will do, but without one the API answers 400.
        request.Headers.Add("MS-CV", Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)) + ".0");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        string json = HttpOutcomes.Classify(response) switch
        {
            HttpOutcome.Ok => await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false),
            HttpOutcome.RateLimited => throw new GamePassException(CoreStrings.FreePlayDays_RateLimited) { IsRateLimited = true },
            // A header or a body it no longer accepts (400 MissingHeader without MS-CV).
            HttpOutcome.BadRequest => throw new GamePassException(CoreStrings.FreePlayDays_Format),
            _ => throw new GamePassException(CoreStrings.FreePlayDays_Down)
        };

        // ponytail: without a previous list (first visit, cache erased), last weekend's games read
        // on Thursday before xbox.com's update count as this week's until the next day's download.
        var games = ParseJson(json, FreePlayDaysEnd(now));
        if (previous is { Games: [{ Until: { } lastEnd }, ..] } && lastEnd <= now
            && games.Select(g => g.Link).ToHashSet().SetEquals(previous.Games.Select(g => g.Link)))
            return games.Select(g => g with { Until = lastEnd }).ToList();
        return games;
    }

    /// <summary>The end of this week's Free Play Days: Sunday 23:59:59 Pacific time, the week running Thursday to Wednesday.</summary>
    internal static DateTimeOffset FreePlayDaysEnd(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Pacific);
        int sinceThursday = ((int)local.DayOfWeek - (int)DayOfWeek.Thursday + 7) % 7;
        var sunday = local.Date.AddDays(3 - sinceThursday).Add(new TimeSpan(23, 59, 59));
        return new DateTimeOffset(sunday, Pacific.GetUtcOffset(sunday));
    }

    /// <summary>The games of the Free Play Days channel (the Game Pass plans it shows left out), free until <paramref name="until"/>.</summary>
    /// <exception cref="GamePassException">The answer has no such channel.</exception>
    internal static IReadOnlyList<FreeGame> ParseJson(string json, DateTimeOffset until)
    {
        BrowseResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, GamePassJsonContext.Default.BrowseResponseDto);
        }
        catch (JsonException)
        {
            dto = null;
        }
        var order = dto?.Channels?.GetValueOrDefault(ChannelKey)?.Products
            ?? throw new GamePassException(CoreStrings.FreePlayDays_Format);

        var summaries = (dto.ProductSummaries ?? [])
            .Where(p => p.ProductId is not null)
            .DistinctBy(p => p.ProductId)
            .ToDictionary(p => p.ProductId!);
        var games = new List<FreeGame>();
        foreach (var id in order.Select(p => p.ProductId))
        {
            if (id is null || summaries.GetValueOrDefault(id) is not { ProductKind: "Game", Title: { } title } summary || string.IsNullOrWhiteSpace(title))
                continue;
            string? image = summary.Images?.SuperHeroArt?.Url ?? summary.Images?.BoxArt?.Url;
            games.Add(new FreeGame(title.Trim(), StorePage + Uri.EscapeDataString(id), until, image, PlayOnly: true));
        }
        return games;
    }
}
