namespace GameRadar.Core.Services;

/// <summary>An online service's error the user can be told about (translated); one subclass per connector.</summary>
public abstract class ServiceException : Exception
{
    protected ServiceException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    /// <summary>The service limits the requests for now: a lookup stops there instead of asking every game.</summary>
    public bool IsRateLimited { get; init; }
}
