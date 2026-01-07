namespace Ledger.Application.Exceptions;

/// <summary>
/// Exception thrown when validation fails.
/// Maps to HTTP 400 Bad Request.
/// </summary>
public class ValidationException : Exception
{
    public string ReasonCode { get; }

    public ValidationException(string message, string reasonCode = "VALIDATION_ERROR")
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    public ValidationException(string message, Exception innerException, string reasonCode = "VALIDATION_ERROR")
        : base(message, innerException)
    {
        ReasonCode = reasonCode;
    }
}

