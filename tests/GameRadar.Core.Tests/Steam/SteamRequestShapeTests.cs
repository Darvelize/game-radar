using System.Net;
using System.Text;
using GameRadar.Core.Services;
using GameRadar.Core.Steam;

namespace GameRadar.Core.Tests.Steam;

/// <summary>
/// The JSON the Steam store queries send, byte for byte (input_json): the
/// API reads these names, so a change of serializer must not move one.
/// </summary>
public class SteamRequestShapeTests
{
    private const string Context = "\"context\":{\"language\":\"english\",\"country_code\":\"FR\"}";

    [Fact]
    public async Task TheCatalogQuery_SendsTheTopSellersRequest()
    {
        var inputs = new List<string>();
        using var http = new HttpClient(new CapturingHandler(inputs, uri => uri.AbsolutePath.Contains("GetTagList")
            ? """{"response":{"tags":[{"tagid":1,"name":"Action"}]}}"""
            : """{"response":{"store_items":[]}}"""));

        await new SteamCatalogClient(http).FetchCatalogAsync();

        Assert.Equal(
            "{\"query\":{\"start\":0,\"count\":" + SteamCatalogClient.PageSize + ",\"sort\":11,"
            + "\"filters\":{\"type_filters\":{\"include_games\":true}}},"
            + Context + ",\"data_request\":{\"include_tag_count\":20,\"include_reviews\":true,\"include_assets\":true}}",
            inputs[0]);
        Assert.Contains("\"start\":" + SteamCatalogClient.PageSize + ",", inputs[1]);
    }

    [Fact]
    public async Task TheFreeGames_SendTheGivenAwayThenTheTopSellersRequests()
    {
        var inputs = new List<string>();
        using var http = new HttpClient(new CapturingHandler(inputs, _ => """{"response":{"store_items":[]}}"""));

        await new SteamFreeGamesClient(http).FetchAsync(TestData.Now);

        // Given away: 100 % off is ignored by the API (measured), 99 % gives exactly those.
        Assert.Equal(
            "{\"query\":{\"start\":0,\"count\":" + SteamCatalogClient.PageSize + ",\"sort\":11,"
            + "\"filters\":{\"type_filters\":{\"include_games\":true},\"price_filters\":{\"min_discount_percent\":99}}},"
            + Context + ",\"data_request\":{\"include_assets\":true}}",
            inputs[0]);
        // Free weekends: no filter for them, read in the best sellers, without pictures (half the weight).
        Assert.Equal(
            "{\"query\":{\"start\":0,\"count\":" + SteamCatalogClient.PageSize + ",\"sort\":11,"
            + "\"filters\":{\"type_filters\":{\"include_games\":true}}},"
            + Context + ",\"data_request\":{}}",
            inputs[1]);
        Assert.Contains("\"start\":" + SteamCatalogClient.PageSize + ",", inputs[2]);
        // No free weekend found: no picture asked.
        Assert.Equal(3, inputs.Count);
    }

    [Fact]
    public async Task SuggestGames_SendsTheSearchRequest()
    {
        var inputs = new List<string>();
        using var http = new HttpClient(new CapturingHandler(inputs, _ => """{"response":{"store_items":[]}}"""));

        await new SteamTagClient(http).SuggestGamesAsync("  Hades ", 5);

        Assert.Equal(
            "{\"search_term\":\"Hades\",\"max_results\":5," + Context + ",\"data_request\":{\"include_tag_count\":20}}",
            Assert.Single(inputs));
    }

    [Fact]
    public async Task SearchByTitle_SendsTheCleanedTitle()
    {
        var inputs = new List<string>();
        using var http = new HttpClient(new CapturingHandler(inputs, _ => """{"response":{"store_items":[]}}"""));

        await new SteamTagClient(http).SearchByTitleAsync("Hades");

        Assert.Equal(
            "{\"search_term\":\"" + GameTitle.Clean("Hades") + "\",\"max_results\":5," + Context
            + ",\"data_request\":{\"include_tag_count\":20}}",
            Assert.Single(inputs));
    }

    [Fact]
    public async Task FetchByAppIds_SendsOneIdObjectPerGame()
    {
        var inputs = new List<string>();
        using var http = new HttpClient(new CapturingHandler(inputs, _ => """{"response":{"store_items":[]}}"""));

        await new SteamTagClient(http).FetchByAppIdsAsync(["1145360", "10"]);

        Assert.Equal(
            "{\"ids\":[{\"appid\":1145360},{\"appid\":10}]," + Context + ",\"data_request\":{\"include_tag_count\":20}}",
            Assert.Single(inputs));
    }

    /// <summary>Records the decoded input_json of each query, answers with the given JSON.</summary>
    private sealed class CapturingHandler(List<string> inputs, Func<Uri, string> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            const string marker = "input_json=";
            int at = uri.Query.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
                inputs.Add(Uri.UnescapeDataString(uri.Query[(at + marker.Length)..]));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answer(uri), Encoding.UTF8, "application/json")
            });
        }
    }
}
