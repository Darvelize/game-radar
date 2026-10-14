using System.Net;

namespace GameRadar.Core.Tests.PlayStation;

/// <summary>The PlayStation Store's site answering one status and body; the last request kept to check it.</summary>
internal sealed class PlayStationFakeStore(HttpStatusCode status, string body) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }

    // Read while the request is alive: the client disposes it once sent.
    public string? ContentType { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Request = request;
        ContentType = request.Content?.Headers.ContentType?.MediaType;
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
