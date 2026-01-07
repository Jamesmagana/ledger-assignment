namespace Ledger.Application.Exceptions;

/// <summary>
/// Exception thrown when a conflict occurs (e.g., duplicate resource).
/// Maps to HTTP 409 Conflict.
/// </summary>
public class ConflictException : Exception
{
    public string ReasonCode { get; }

    public ConflictException(string message, string reasonCode = "CONFLICT")
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    public ConflictException(string message, Exception innerException, string reasonCode = "CONFLICT")
        : base(message, innerException)
    {
        ReasonCode = reasonCode;
    }
}

