using System.Net;

namespace GameRadar.Core.Services;

/// <summary>What an HTTP status means for a store or API client, before it picks its own message.</summary>
internal enum HttpOutcome
{
    /// <summary>2xx.</summary>
    Ok,
    /// <summary>404: no such page or game.</summary>
    NotFound,
    /// <summary>429: too many requests.</summary>
    RateLimited,
    /// <summary>401.</summary>
    Unauthorized,
    /// <summary>403: a rate limit, a refused key or a private profile, depending on the API.</summary>
    Forbidden,
    /// <summary>400: an id or a request the API cannot read.</summary>
    BadRequest,
    /// <summary>5xx: the service is down (never "this game has no page").</summary>
    ServerError,
    /// <summary>Any other status (redirects, 409, 418...): each client keeps its own handling.</summary>
    Other,
}

/// <summary>
/// The one place that names the HTTP statuses the clients tell apart. It only
/// gives the category: what a 403 MEANS (a rate limit for the Steam store, a
/// refused key for its Web API, a private profile for achievements) and which
/// message to show stay with each client.
/// </summary>
internal static class HttpOutcomes
{
    public static HttpOutcome Classify(HttpStatusCode status) => status switch
    {
        >= (HttpStatusCode)200 and < (HttpStatusCode)300 => HttpOutcome.Ok,
        HttpStatusCode.NotFound => HttpOutcome.NotFound,
        HttpStatusCode.TooManyRequests => HttpOutcome.RateLimited,
        HttpStatusCode.Unauthorized => HttpOutcome.Unauthorized,
        HttpStatusCode.Forbidden => HttpOutcome.Forbidden,
        HttpStatusCode.BadRequest => HttpOutcome.BadRequest,
        >= (HttpStatusCode)500 => HttpOutcome.ServerError,
        _ => HttpOutcome.Other,
    };

    public static HttpOutcome Classify(HttpResponseMessage response) => Classify(response.StatusCode);
}
