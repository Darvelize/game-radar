using System.Net;
using System.Text;

namespace GameRadar.App.Tests;

/// <summary>
/// The Steam store (tags of app 100, no title found) and the Game Pass
/// lists; each can be held until <see cref="Release"/>. Anything else: 404.
/// </summary>
internal sealed class FakeStores : HttpMessageHandler
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool HoldTags { get; init; }
    public bool HoldGamePass { get; init; }
    public TaskCompletionSource TagsReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource GamePassReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _released.TrySetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        if (uri.AbsolutePath.Contains("GetItems") || uri.AbsolutePath.Contains("SearchSuggestions"))
        {
            if (HoldTags)
            {
                TagsReached.TrySetResult();
                await _released.Task.WaitAsync(ct);
            }
            return Json(uri.AbsolutePath.Contains("GetItems")
                ? """{ "response": { "store_items": [ { "success": 1, "appid": 100, "name": "Hero Quest", "tags": [ { "tagid": 122, "weight": 900 } ] } ] } }"""
                : """{ "response": { "store_items": [] } }""");
        }
        if (uri.Host == "catalog.gamepass.com" && HoldGamePass)
        {
            GamePassReached.TrySetResult();
            await _released.Task.WaitAsync(ct);
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>A fake server's JSON answer (every fake network of the App tests).</summary>
    internal static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
