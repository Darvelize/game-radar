using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GameRadar.Core.Cache;

namespace GameRadar.Core.Services;

/// <summary>
/// The lookups of genres and tags (Steam store, Microsoft Store) only make the
/// recommendations finer: when a store fails, they stop, keep what they found,
/// and look up the rest next time. The recommendations go on without them.
/// </summary>
public static class StoreLookup
{
    /// <summary>
    /// A store refused (rate limit, PlayStation Store down: any <see cref="ServiceException"/>), or
    /// <see cref="Transient"/>. Only a cancellation by <paramref name="ct"/> is not a failure: it goes up.
    /// </summary>
    public static bool Failed(Exception ex, CancellationToken ct) =>
        ex is ServiceException || Transient(ex, ct);

    /// <summary>
    /// The network, not the service: unreachable, an unexpected answer, or
    /// too slow (the HttpClient timeout, not <paramref name="ct"/>).
    /// </summary>
    private static bool Transient(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    /// <summary>No answer at all: unreachable, or too slow (the HttpClient timeout, not <paramref name="ct"/>).</summary>
    private static bool Unreachable(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException { StatusCode: null }
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    /// <summary>
    /// The details of a history (achievements, trophies), one request per item,
    /// <paramref name="maxAtOnce"/> at a time: the same rule for every platform. An item
    /// whose answer fails (the service refused or broke it, an odd answer) is skipped and
    /// asked again at the next sync; the network failing (no answer, each request waiting
    /// up to 30 s) skips the items not started yet; an ended session or the rate limit go up.
    /// </summary>
    internal static async Task FetchEachAsync<TItem>(
        IEnumerable<TItem> items, int maxAtOnce, Func<TItem, CancellationToken, Task> fetchOne, CancellationToken ct)
    {
        bool unreachable = false;
        await Parallel.ForEachAsync(items,
            new ParallelOptions { MaxDegreeOfParallelism = maxAtOnce, CancellationToken = ct },
            async (item, token) =>
            {
                if (Volatile.Read(ref unreachable))
                    return;
                try
                {
                    await fetchOne(item, token).ConfigureAwait(false);
                }
                // Against ct, not token: the requests that an error going up stops land
                // here too, so only that error goes up.
                catch (Exception ex) when (Unreachable(ex, ct))
                {
                    Volatile.Write(ref unreachable, true);
                }
                catch (Exception ex) when (ex is ServiceException { IsRateLimited: false, EndsSession: false } || Transient(ex, ct))
                {
                }
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// One request per item, <paramref name="maxAtOnce"/> at a time, answers in the items'
    /// order. An answer that does not come (the service fails for it, the network) is
    /// null and the item is kept; the rate limit stops everything. Not one answer: the
    /// query broke, <paramref name="whenNoneRead"/> is thrown.
    /// </summary>
    internal static async Task<TResult?[]> FetchAllAsync<TItem, TResult>(
        IReadOnlyList<TItem> items, int maxAtOnce, Func<TItem, CancellationToken, Task<TResult?>> fetchOne,
        Func<Exception> whenNoneRead, CancellationToken ct)
    {
        var answers = new TResult?[items.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = maxAtOnce, CancellationToken = ct },
            async (i, token) =>
            {
                try
                {
                    answers[i] = await fetchOne(items[i], token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ServiceException { IsRateLimited: false } || Transient(ex, token))
                {
                }
            }).ConfigureAwait(false);
        if (items.Count > 0 && answers.All(a => a is null))
            throw whenNoneRead();
        return answers;
    }

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
