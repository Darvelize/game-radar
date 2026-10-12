namespace GameRadar.Core.EA;

/// <summary>
/// Error raised when EA answers with a problem we can explain to the user
/// (token expired, wrong text pasted...). A dedicated type lets the UI catch
/// it separately from unexpected bugs.
/// </summary>
public sealed class EaException : Exception
{
    public EaException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    /// <summary>EA refused the token (expired, or not a token): it must be pasted again.</summary>
    public bool IsTokenRefused { get; init; }

    /// <summary>EA asks to wait: every other request would be refused too.</summary>
    public bool IsRateLimited { get; init; }
}
