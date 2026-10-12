using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GameRadar.Core.Settings;

/// <summary>
/// One secret kept by <see cref="SecretStore"/>: its file, and its DPAPI
/// "entropy", an extra input of the encryption so that another program of
/// the same Windows user calling DPAPI without it cannot decrypt the file.
/// </summary>
internal sealed record Secret(string FileName, string Entropy)
{
    public static readonly Secret XboxAccount = new("xbox-account.bin", "GameRadar.XboxAccount.v1");
    public static readonly Secret SteamAccount = new("steam-account.bin", "GameRadar.SteamAccount.v1");
    public static readonly Secret PlayStationAccount = new("playstation-account.bin", "GameRadar.PlayStationAccount.v1");
    public static readonly Secret EpicAccount = new("epic-account.bin", "GameRadar.EpicAccount.v1");

    public static readonly Secret[] All = [XboxAccount, SteamAccount, PlayStationAccount, EpicAccount];
}

/// <summary>
/// Saves secrets (Xbox session, Steam API key, PlayStation NPSSO) in %LOCALAPPDATA%\GameRadar,
/// encrypted with DPAPI (Data Protection API): Windows derives the key from
/// the user's session, so the files are unreadable from another Windows
/// account or another computer.
/// Elsewhere than on Windows, nothing is saved (to enter again at each launch).
/// </summary>
public sealed class SecretStore
{
    private readonly string _folder;

    // Tripped by EraseAll (shared with the other stores when the app gives its own).
    private readonly EraseGuard _guard;

    /// <param name="directory">Custom folder, mainly for tests. Null = default location.</param>
    /// <param name="guard">Shared "erased" state of the app's stores. Null = this store's own.</param>
    public SecretStore(string? directory = null, EraseGuard? guard = null)
    {
        _folder = directory ?? DataFolder.Default.Root;
        _guard = guard ?? new EraseGuard();
    }

    /// <summary>The saved value, or null. Never throws: an unreadable file counts as missing.</summary>
    internal T? Load<T>(Secret secret, JsonTypeInfo<T> typeInfo) where T : class
    {
        string path = PathOf(secret);
        // The guard also tells the compiler's platform analyzer (CA1416)
        // that the Windows-only API below is safe to call.
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
            return null;

        try
        {
            byte[] json = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy(secret), DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    internal void Save<T>(Secret secret, T value, JsonTypeInfo<T> typeInfo)
    {
        if (!OperatingSystem.IsWindows())
            return;

        byte[] encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo), Entropy(secret), DataProtectionScope.CurrentUser);

        // Temp file then swap, like the settings: never a half-written file,
        // and nothing written once the data were erased. The callers are
        // synchronous; LocalFiles never resumes on a captured context
        // (ConfigureAwait(false) all the way), so waiting here cannot deadlock.
        LocalFiles.WriteAtomicallyAsync(
            PathOf(secret), (file, token) => file.WriteAsync(encrypted, token).AsTask(), _guard, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    /// <summary>Deletes a secret, and a temp file left by a crash; nothing to delete is fine (signed out twice, no folder).</summary>
    internal void Delete(Secret secret) => LocalFiles.Delete(PathOf(secret));

    /// <summary>
    /// Deletes every secret file, then the folder when nothing else is left
    /// in it (the image cache must be erased first, see <see cref="LocalDataEraser"/>).
    /// </summary>
    public void EraseAll()
    {
        _guard.Trip();
        foreach (var secret in Secret.All)
        {
            // The temp files of a save (one per write, a unique name, or ".tmp" of an older version).
            LocalFiles.DeleteMatching(_folder, secret.FileName + ".*tmp");
            LocalFiles.Delete(PathOf(secret));
        }
        LocalFiles.DeleteFolderIfEmpty(_folder);
    }

    private string PathOf(Secret secret) => Path.Combine(_folder, secret.FileName);

    private static byte[] Entropy(Secret secret) => Encoding.UTF8.GetBytes(secret.Entropy);
}
