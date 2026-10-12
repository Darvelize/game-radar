namespace GameRadar.Core.Steam;

/// <summary>
/// Error raised when Steam answers with a problem we can explain to the user
/// (key refused, private profile...). A dedicated type lets the UI catch it
/// separately from unexpected bugs.
/// </summary>
public sealed class SteamException : Exception
{
    public SteamException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
