using GameRadar.Core.Services;

namespace GameRadar.Core.Xbox;

/// <summary>
/// Error raised when Microsoft or Xbox Live answers with a problem we can
/// explain to the user (no Xbox profile, expired session...). A dedicated
/// type lets the UI catch it separately from unexpected bugs.
/// </summary>
public sealed class XboxLiveException : ServiceException
{
    public XboxLiveException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
