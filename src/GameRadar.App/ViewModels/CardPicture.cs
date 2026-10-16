using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GameRadar.Core.Cache;

namespace GameRadar.App.ViewModels;

/// <summary>
/// A card with a picture ("Pour toi", "Jeux gratuits"): its address, the decoded
/// picture once loaded, and its release when the card goes away.
/// </summary>
public abstract partial class PictureCard : ObservableObject
{
    // The load of its picture, started once; cancelled when the card goes away.
    private CancellationTokenSource? _picture;

    /// <summary>Resized picture address, or null when the game has none.</summary>
    public string? ImageUrl { get; protected init; }

    /// <summary>Set once downloaded and decoded; until then the card shows its placeholder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial Bitmap? Image { get; set; }

    public bool HasImage => Image is not null;

    /// <summary>
    /// Loads its picture, once: a card kept from one display to the next is not loaded again.
    /// Never throws: a picture that fails keeps the placeholder.
    /// </summary>
    /// <param name="ct">Cancelled when newer cards replace a whole batch.</param>
    internal async Task LoadPictureAsync(ImageFileCache cache, SemaphoreSlim downloads, CancellationToken ct)
    {
        if (ImageUrl is null || _picture is not null)
            return;
        _picture = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Back on the UI thread; null when the card went away meanwhile.
        if (await CardPicture.LoadAsync(cache, ImageUrl, downloads, _picture.Token) is { } bitmap)
            Image = bitmap;
    }

    /// <summary>
    /// The card goes away: its picture is freed (a Bitmap holds native memory that the
    /// garbage collector does not see), and one still loading is stopped or freed on arrival.
    /// </summary>
    public void ReleaseImage()
    {
        _picture?.Cancel();
        var image = Image;
        Image = null;
        image?.Dispose();
    }
}

/// <summary>The picture of a card: from disk or downloaded, then decoded; and the store pages the cards open.</summary>
internal static class CardPicture
{
    /// <summary>Card width the pictures are decoded to: only the pixels the card needs stay in memory.</summary>
    public const int DecodeWidth = 560;

    // Twice the card width (280 px), so the picture stays sharp on high-DPI
    // screens; the image server resizes on the fly (~30 KB instead of ~250 KB).
    /// <summary>The Microsoft and PlayStation image servers' resizing.</summary>
    public const string ServerSize = "w=560&h=315&q=80";

    /// <summary>Epic's CDN resizing, with its own parameters (946 KB → 49 KB, measured).</summary>
    public const string EpicSize = "resize=1&w=640&h=360&quality=medium";

    /// <summary>The picture address with the server's resizing parameters.</summary>
    public static string Resized(string url, string size) => url + (url.Contains('?') ? "&" : "?") + size;

    /// <summary>
    /// A store page from a link of an answer or a cache file: only an https page of one of
    /// <paramref name="hosts"/>, in the app's language when the site has one in its path
    /// ("…/p/slug" → "…/fr/p/slug"); null otherwise.
    /// </summary>
    public static Uri? StorePage(string? link, string? locale, params string[] hosts) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && hosts.Contains(uri.Host)
            ? locale is null ? uri : new Uri($"https://{uri.Host}/{locale}{uri.PathAndQuery}")
            : null;

    /// <summary>
    /// The pictures of these cards not loaded yet, a few downloads at a time, in the cards'
    /// order: when <paramref name="ct"/> is cancelled (newer cards), those not started are never made.
    /// </summary>
    public static async Task LoadAllAsync(ImageFileCache cache, IEnumerable<PictureCard> cards, CancellationToken ct)
    {
        using var downloads = new SemaphoreSlim(ImageFileCache.MaxParallelDownloads);
        await Task.WhenAll(cards.Select(card => card.LoadPictureAsync(cache, downloads, ct)));
    }

    /// <summary>
    /// The decoded picture, or null when it failed (corrupted file, unsupported format...: the
    /// card keeps its placeholder) or was cancelled. Never throws. A picture decoded after the
    /// cancellation is freed at once: nobody will show it.
    /// </summary>
    /// <param name="downloads">Shared by the cards of one batch: a few downloads at a time.</param>
    public static async Task<Bitmap?> LoadAsync(ImageFileCache cache, string url, SemaphoreSlim downloads, CancellationToken ct)
    {
        try
        {
            // Already on disk: at once, never behind the downloads.
            string? path = cache.CachedPath(url) ?? await DownloadAsync();
            if (path is null || ct.IsCancellationRequested)
                return null;

            // Decoding a JPEG is CPU work: off the UI thread.
            var bitmap = await Task.Run(() =>
            {
                ImageFileCache.MarkShown(path);
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, DecodeWidth);
            });
            if (!ct.IsCancellationRequested)
                return bitmap;
            bitmap.Dispose();
            return null;
        }
        catch (Exception)
        {
            return null;
        }

        // A started download is not stopped: ImageFileCache shares it with every card of the same game.
        async Task<string?> DownloadAsync()
        {
            await downloads.WaitAsync(ct);
            try
            {
                return await cache.GetAsync(url);
            }
            finally
            {
                downloads.Release();
            }
        }
    }
}
