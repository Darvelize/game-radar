using GameRadar.Core.Settings;

namespace GameRadar.Core.Steam;

/// <summary>What is kept between sessions. The key is a secret: encrypted on disk.</summary>
internal sealed record SavedSteamAccount(string ApiKey, string SteamId, string? PersonaName);

/// <summary>
/// The user's Steam account: their own Web API key and their profile.
/// Steam offers no sign-in for third-party apps, hence the pasted key.
/// </summary>
public sealed class SteamAccount
{
    private readonly SecretStore _store;
    private SavedSteamAccount? _saved;

    public SteamAccount(SecretStore store)
    {
        _store = store;
        _saved = store.Load(Secret.SteamAccount, SteamJsonContext.Default.SavedSteamAccount) is { ApiKey.Length: > 0, SteamId.Length: > 0 } saved
            ? saved
            : null;
    }

    public bool IsConnected => _saved is not null;

    public string? SteamId => _saved?.SteamId;

    public string? PersonaName => _saved?.PersonaName;

    /// <summary>
    /// Checks the key and the profile (1 or 2 requests), then saves them.
    /// Throws <see cref="SteamException"/> with an explanation when one is wrong.
    /// </summary>
    public async Task ConnectAsync(HttpClient http, string apiKey, string profile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new SteamException(CoreStrings.Steam_PasteKey);

        var client = new SteamClient(http, apiKey);
        string steamId = await client.ResolveSteamIdAsync(profile, ct).ConfigureAwait(false);
        // Also proves the key works, even when the profile needed no request.
        string? name = await client.FetchPersonaNameAsync(steamId, ct).ConfigureAwait(false);

        var saved = new SavedSteamAccount(apiKey.Trim(), steamId, name);
        _store.Save(Secret.SteamAccount, saved, SteamJsonContext.Default.SavedSteamAccount);
        _saved = saved;
    }

    /// <summary>A client using the saved key.</summary>
    public SteamClient CreateClient(HttpClient http) =>
        new(http, _saved?.ApiKey ?? throw new SteamException(CoreStrings.Steam_NotConnected));

    /// <summary>Forgets the key on this computer (the library cache is kept).</summary>
    public void Disconnect()
    {
        _store.Delete(Secret.SteamAccount);
        _saved = null;
    }
}
