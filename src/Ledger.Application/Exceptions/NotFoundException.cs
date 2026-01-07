namespace Ledger.Application.Exceptions;

/// <summary>
/// Exception thrown when a requested resource is not found.
/// Maps to HTTP 404 Not Found.
/// </summary>
public class NotFoundException : Exception
{
    public string ReasonCode { get; }

    public NotFoundException(string message, string reasonCode = "NOT_FOUND")
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    public NotFoundException(string message, Exception innerException, string reasonCode = "NOT_FOUND")
        : base(message, innerException)
    {
        ReasonCode = reasonCode;
    }
}

