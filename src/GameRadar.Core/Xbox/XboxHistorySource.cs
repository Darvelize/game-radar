using System.Text.Json;
using GameRadar.Core.Cache;
using GameRadar.Core.Models;

namespace GameRadar.Core.Xbox;

/// <summary>
/// Gives the Xbox game history, from the local cache or from Xbox Live.
/// Xbox Live is only called on explicit refresh by the user.
/// </summary>
public sealed class XboxHistorySource
{
    private const string CacheKey = "xbox-history";

    private readonly CacheStore _cache;

    public XboxHistorySource(CacheStore cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <summary>
    /// Returns the last saved history, or null when there is none.
    /// No network call. Never throws: a corrupted cache counts as missing.
    /// </summary>
    public GameHistory? LoadCached()
    {
        if (_cache.TryLoad(CacheKey) is not { } entry)
            return null;

        try
        {
            return new GameHistory(XboxLiveClient.ParseJson(entry.Content), entry.SavedAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches the history from Xbox Live and replaces the cache.
    /// </summary>
    public async Task<GameHistory> RefreshAsync(XboxLiveClient client, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        string json = await client.FetchHistoryJsonAsync(ct).ConfigureAwait(false);

        // Parse BEFORE saving: an unexpected answer throws here and the
        // previous, valid cache is kept instead of being overwritten.
        var games = XboxLiveClient.ParseFetchedJson(json);
        await _cache.SaveAsync(CacheKey, json, ct).ConfigureAwait(false);

        return new GameHistory(games, DateTimeOffset.UtcNow);
    }
}
