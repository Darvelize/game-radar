using GameRadar.Core.Services;

namespace GameRadar.Core.Amazon;

/// <summary>
/// Error raised when Amazon's free games list answers with a problem we can
/// explain to the user. A dedicated type lets the UI catch it separately from
/// unexpected bugs.
/// </summary>
public sealed class AmazonException : ServiceException
{
    public AmazonException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    /// <summary>Amazon asks to wait.</summary>
    internal static AmazonException RateLimited() => new(CoreStrings.Amazon_RateLimited) { IsRateLimited = true };
}
