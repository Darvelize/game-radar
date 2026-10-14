using GameRadar.Core.Services;

namespace GameRadar.Core.GamePass;

/// <summary>
/// Error raised when the Microsoft catalog endpoints answer with a problem
/// we can explain to the user (message in French).
/// </summary>
public sealed class GamePassException : ServiceException
{
    public GamePassException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
