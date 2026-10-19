using GameRadar.Core.Services;

namespace GameRadar.Core.EA;

/// <summary>
/// Error raised when EA answers with a problem we can explain to the user
/// (token expired, wrong text pasted...). A dedicated type lets the UI catch
/// it separately from unexpected bugs.
/// </summary>
public sealed class EaException : ServiceException
{
    public EaException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
