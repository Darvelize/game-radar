using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GameRadar.Core.Cache;
using GameRadar.Core.GamePass;
using GameRadar.Core.PlayStation;
using GameRadar.Core.Steam;

namespace GameRadar.Core.Services;

/// <summary>
/// The lookups of genres and tags (Steam store, Microsoft Store) only make the
/// recommendations finer: when a store fails, they stop, keep what they found,
/// and look up the rest next time. The recommendations go on without them.
/// </summary>
public static class StoreLookup
{
    /// <summary>
    /// A store refused (rate limit, PlayStation Store down), or <see cref="Transient"/>. Only a
    /// cancellation by <paramref name="ct"/> is not a failure: it goes up.
    /// </summary>
    public static bool Failed(Exception ex, CancellationToken ct) =>
        ex is SteamException or GamePassException or PlayStationException || Transient(ex, ct);

    /// <summary>
    /// The network, not the service: unreachable, an unexpected answer, or
    /// too slow (the HttpClient timeout, not <paramref name="ct"/>).
    /// </summary>
    public static bool Transient(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    /// <summary>
    /// Runs a lookup that fills <c>answers</c>, stopped quietly when a store
    /// fails. Its answers are saved whatever happens (an error, a
    /// cancellation: the lookups done are not repeated), added to the cache
    /// as it is now (a newer run may have saved meanwhile, this one being
    /// cancelled), and added to <paramref name="known"/>.
    /// </summary>
    /// <param name="typeInfo">The generated JSON description of the cache (a dictionary of the entries).</param>
    internal static async Task RunAsync<T>(
        CacheStore cache, string cacheKey, Dictionary<string, T> known,
        Func<Dictionary<string, T>, Task> lookUp, JsonTypeInfo<Dictionary<string, T>> typeInfo, CancellationToken ct)
    {
        var answers = new Dictionary<string, T>();
        try
        {
            await lookUp(answers).ConfigureAwait(false);
        }
        catch (Exception ex) when (Failed(ex, ct))
        {
        }
        finally
        {
            foreach (var (id, entry) in answers)
                known[id] = entry;
            await cache.MergeDictionaryAsync(cacheKey, answers, typeInfo).ConfigureAwait(false);
        }
    }
}
