namespace Ledger.Application.Exceptions;

/// <summary>
/// Exception thrown when authentication is required or fails.
/// Maps to HTTP 401 Unauthorized.
/// </summary>
public class UnauthorizedException : Exception
{
    public string ReasonCode { get; }

    public UnauthorizedException(string message, string reasonCode = "UNAUTHORIZED")
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    public UnauthorizedException(string message, Exception innerException, string reasonCode = "UNAUTHORIZED")
        : base(message, innerException)
    {
        ReasonCode = reasonCode;
    }
}

