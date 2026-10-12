namespace GameRadar.Core.Epic;

/// <summary>
/// Error raised when Epic answers with a problem we can explain to the user
/// (code expired, wrong text pasted...). A dedicated type lets the UI catch
/// it separately from unexpected bugs.
/// </summary>
public sealed class EpicException : Exception
{
    public EpicException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    /// <summary>Epic refused the session (expired, revoked): a new code must be pasted.</summary>
    public bool IsTokenRefused { get; init; }

    /// <summary>Epic asks to wait: every other request would be refused too.</summary>
    public bool IsRateLimited { get; init; }

    /// <summary>The session is gone (expired a year unused, revoked): a new code must be pasted.</summary>
    internal static EpicException SessionExpired() => new(CoreStrings.Epic_SessionExpired) { IsTokenRefused = true };

    /// <summary>Epic asks to wait: every other request would be refused too.</summary>
    internal static EpicException RateLimited() => new(CoreStrings.Epic_RateLimited) { IsRateLimited = true };
}
