using System.Net;
using GameRadar.Core.Services;

namespace GameRadar.Core.Tests.Services;

public class HttpOutcomesTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, "Ok")]
    [InlineData(HttpStatusCode.NoContent, "Ok")]
    [InlineData(HttpStatusCode.NotFound, "NotFound")]
    [InlineData(HttpStatusCode.TooManyRequests, "RateLimited")]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData(HttpStatusCode.BadRequest, "BadRequest")]
    [InlineData(HttpStatusCode.InternalServerError, "ServerError")]
    [InlineData(HttpStatusCode.BadGateway, "ServerError")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServerError")]
    [InlineData(HttpStatusCode.MovedPermanently, "Other")]
    [InlineData(HttpStatusCode.Conflict, "Other")]
    [InlineData((HttpStatusCode)418, "Other")]
    public void Classify_GivesTheCategoryOfTheStatus(HttpStatusCode status, string expected)
    {
        // The enum is internal: compared by name.
        Assert.Equal(expected, HttpOutcomes.Classify(status).ToString());
    }
}
