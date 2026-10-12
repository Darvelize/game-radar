using System.Net;
using System.Text.RegularExpressions;
using GameRadar.Core.PlayStation;

namespace GameRadar.Core.Tests.PlayStation;

/// <summary>
/// The persisted-query parameters the PlayStation Store site receives (variables
/// and extensions), byte for byte: the site reads these names, so a change of
/// serializer must not move one.
/// </summary>
public class PlayStationRequestShapeTests
{
    [Fact]
    public async Task TheSearch_SendsItsVariablesAndTheHashOfItsPersistedQuery()
    {
        var handler = new CapturingHandler();

        await new PlayStationStoreClient(new HttpClient(handler)).SearchGamesAsync("  astro ");

        Assert.Equal(
            "{\"countryCode\":\"GB\",\"languageCode\":\"en\",\"nextCursor\":\"\",\"pageOffset\":0,\"pageSize\":24,\"searchTerm\":\"astro\"}",
            handler.Parameter("variables"));
        Assert.Matches(new Regex("^\\{\"persistedQuery\":\\{\"version\":1,\"sha256Hash\":\"[0-9a-f]{64}\"\\}\\}$"), handler.Parameter("extensions"));
        Assert.Equal("getSearchResults", handler.Parameter("operationName"));
    }

    [Fact]
    public async Task TheGenresLookup_SendsTheProductId()
    {
        var handler = new CapturingHandler();

        await new PlayStationStoreClient(new HttpClient(handler)).FetchGenresAsync("EP9000-PPSA21567_00-0000000000000000");

        Assert.Equal("{\"productId\":\"EP9000-PPSA21567_00-0000000000000000\"}", handler.Parameter("variables"));
        Assert.Matches(new Regex("^\\{\"persistedQuery\":\\{\"version\":1,\"sha256Hash\":\"[0-9a-f]{64}\"\\}\\}$"), handler.Parameter("extensions"));
    }

    /// <summary>Keeps the query string of the request; answers with an empty but valid body.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private string _query = string.Empty;

        public string Parameter(string name)
        {
            foreach (string pair in _query.TrimStart('?').Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (pair[..equals] == name)
                    return Uri.UnescapeDataString(pair[(equals + 1)..]);
            }
            throw new InvalidOperationException($"No parameter {name} in {_query}");
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _query = request.RequestUri!.Query;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"universalSearch":{"results":[]},"productRetrieve":{}}}""")
            });
        }
    }
}
