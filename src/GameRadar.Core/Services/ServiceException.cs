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

    /// <summary>Raised while signing in or renewing the session (blocked, or the service down): every next request needs it.</summary>
    public bool IsSignInError { get; internal set; }

    /// <summary>Nothing more can be asked for now: the session or key is refused, or could not be renewed.</summary>
    public virtual bool EndsSession => IsSignInError;
}
