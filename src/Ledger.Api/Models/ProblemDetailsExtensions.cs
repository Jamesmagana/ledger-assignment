using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api.Models;

/// <summary>
/// Extension methods for ProblemDetails to add custom properties
/// required by RFC7807 and our API contract (reasonCode and correlationId).
/// </summary>
public static class ProblemDetailsExtensions
{
    private const string ReasonCodeKey = "reasonCode";
    private const string CorrelationIdKey = "correlationId";

    /// <summary>
    /// Adds a machine-readable reason code to the ProblemDetails.
    /// </summary>
    public static ProblemDetails WithReasonCode(this ProblemDetails problem, string reasonCode)
    {
        if (problem.Extensions == null)
        {
            problem.Extensions = new Dictionary<string, object?>();
        }

        problem.Extensions[ReasonCodeKey] = reasonCode;
        return problem;
    }

    /// <summary>
    /// Adds a correlation ID to the ProblemDetails for request tracing.
    /// </summary>
    public static ProblemDetails WithCorrelationId(this ProblemDetails problem, string correlationId)
    {
        if (problem.Extensions == null)
        {
            problem.Extensions = new Dictionary<string, object?>();
        }

        problem.Extensions[CorrelationIdKey] = correlationId;
        return problem;
    }
}

