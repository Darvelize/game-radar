using System.Text.Json;
using GameRadar.Core.Cache;

namespace GameRadar.Core.Steam;

/// <summary>
/// The Steam wishlist: the raw answer cached as-is (re-read at every use, so a
/// better parsing applies to it too). Steam is only called on explicit
/// refresh by the user: the Steam sync does it (<see cref="SteamHistorySource"/>).
/// </summary>
public sealed class SteamWishlistSource(CacheStore cache)
{
    // No version suffix: the raw answer never changes format.
    internal const string CacheKey = "steam-wishlist";

    /// <summary>
    /// The Steam app ids saved by the last sync; empty when there is none.
    /// No network call. Never throws: a corrupted cache counts as missing.
    /// </summary>
    public IReadOnlySet<string> LoadCached()
    {
        if (cache.TryLoad(CacheKey) is not { } entry)
            return new HashSet<string>();

        try
        {
            return SteamClient.ParseWishlist(entry.Content);
        }
        catch (JsonException)
        {
            return new HashSet<string>();
        }
    }

    /// <summary>
    /// Fetches the wishlist (1 request) and replaces the cache. Parsed BEFORE
    /// saving: an unexpected answer throws and the previous, valid cache is kept.
    /// </summary>
    public async Task<IReadOnlySet<string>> RefreshAsync(SteamClient client, string steamId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        string json = await client.FetchWishlistJsonAsync(steamId, ct).ConfigureAwait(false);
        var appIds = SteamClient.ParseWishlist(json);
        await cache.SaveAsync(CacheKey, json, ct).ConfigureAwait(false);
        return appIds;
    }
}
