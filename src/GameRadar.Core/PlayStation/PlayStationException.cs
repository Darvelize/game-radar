using GameRadar.Core.Resources;
using GameRadar.Core.Services;

namespace GameRadar.Core.PlayStation;

/// <summary>
/// Error raised when Sony or PlayStation Network answers with a problem we
/// can explain to the user (expired NPSSO, rate limit...). A dedicated type
/// lets the UI catch it separately from unexpected bugs.
/// </summary>
public sealed class PlayStationException : ServiceException
{
    public PlayStationException(string message, bool requiresSignIn = false, Exception? inner = null)
        : base(message, inner)
    {
        RequiresSignIn = requiresSignIn;
    }

    /// <summary>
    /// True when the saved NPSSO can no longer be used (expired, signed out
    /// everywhere...): the user must paste a new one.
    /// </summary>
    public bool RequiresSignIn { get; }

    public override bool EndsSession => IsSignInError || RequiresSignIn;

    internal static PlayStationException RateLimited() => new(CoreStrings.PlayStation_RateLimited) { IsRateLimited = true };
}
