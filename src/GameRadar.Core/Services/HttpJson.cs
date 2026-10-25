using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GameRadar.Core.Services;

/// <summary>Reading the JSON of an answer that may be an error page (the sign-ins of Xbox and Sony).</summary>
internal static class HttpJson
{
    // Error answers are not always JSON (or are empty): null rather than throw,
    // so the caller can still report the HTTP status.
    public static async Task<T?> ReadOrNullAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken ct) where T : class
    {
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            return body.Length == 0 ? null : JsonSerializer.Deserialize(body, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
