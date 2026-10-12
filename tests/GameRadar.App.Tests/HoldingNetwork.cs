using System.Net;

namespace GameRadar.App.Tests;

/// <summary>
/// A fake network that holds every request to one host until <see cref="Release"/>
/// (a request in flight: a connection being checked, a picture downloading); the
/// other hosts answer 404, and so do the held ones once released, unless a
/// picture is given: they then answer with it.
/// </summary>
/// <param name="heldPicture">The PNG the held host answers with once released; null: 404.</param>
internal sealed class HoldingNetwork(string heldHost, byte[]? heldPicture = null) : HttpMessageHandler
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a request to the held host is waiting.</summary>
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _released.TrySetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.Host == heldHost)
        {
            Reached.TrySetResult();
            await _released.Task.WaitAsync(ct);
            if (heldPicture is not null)
            {
                var picture = new ByteArrayContent(heldPicture);
                picture.Headers.ContentType = new("image/png");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = picture };
            }
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
