using GameRadar.Core.Services;

namespace GameRadar.Core.Steam;

/// <summary>
/// Error raised when Steam answers with a problem we can explain to the user
/// (key refused, private profile...). A dedicated type lets the UI catch it
/// separately from unexpected bugs.
/// </summary>
public sealed class SteamException : ServiceException
{
    public SteamException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    /// <summary>Steam refused the API key: it must be entered again.</summary>
    public bool IsKeyRefused { get; init; }

    public override bool EndsSession => IsSignInError || IsKeyRefused;
}
