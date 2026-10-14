using GameRadar.Core.Services;

namespace GameRadar.Core.GOG;

/// <summary>
/// Error raised when GOG answers with a problem we can explain to the user
/// (catalog down, unexpected format...). A dedicated type lets the UI catch it
/// separately from unexpected bugs.
/// </summary>
public sealed class GogException : ServiceException
{
    public GogException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
