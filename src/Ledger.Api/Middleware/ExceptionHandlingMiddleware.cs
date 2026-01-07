using Ledger.Api.Models;
using Ledger.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace Ledger.Api.Middleware;

/// <summary>
/// Global exception handling middleware that converts all exceptions
/// to RFC7807 ProblemDetails format with reasonCode and correlationId.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private const string CorrelationIdItemKey = "CorrelationId";
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items[CorrelationIdItemKey]?.ToString() ?? Guid.NewGuid().ToString();

        _logger.LogError(
            exception,
            "Unhandled exception occurred. CorrelationId: {CorrelationId}",
            correlationId);

        var problem = CreateProblemDetails(context, exception, correlationId);

        context.Response.StatusCode = problem.Status ?? (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Write JSON manually to ensure correct content type
        var json = JsonSerializer.Serialize(problem, options);
        await context.Response.WriteAsync(json);
    }

    private ProblemDetails CreateProblemDetails(HttpContext context, Exception exception, string correlationId)
    {
        var problem = new ProblemDetails
        {
            Instance = context.Request.Path,
            Type = GetProblemType(exception)
        };

        // Map exception to HTTP status code and reason code
        switch (exception)
        {
            case ValidationException validationEx:
                problem.Status = (int)HttpStatusCode.BadRequest;
                problem.Title = "Validation Error";
                problem.Detail = validationEx.Message;
                problem.WithReasonCode(validationEx.ReasonCode);
                break;

            case ArgumentException argEx:
                problem.Status = (int)HttpStatusCode.BadRequest;
                problem.Title = "Invalid Argument";
                problem.Detail = argEx.Message;
                problem.WithReasonCode("INVALID_ARGUMENT");
                break;

            case NotFoundException notFoundEx:
                problem.Status = (int)HttpStatusCode.NotFound;
                problem.Title = "Resource Not Found";
                problem.Detail = notFoundEx.Message;
                problem.WithReasonCode(notFoundEx.ReasonCode);
                break;

            case ConflictException conflictEx:
                problem.Status = (int)HttpStatusCode.Conflict;
                problem.Title = "Conflict";
                problem.Detail = conflictEx.Message;
                problem.WithReasonCode(conflictEx.ReasonCode);
                break;

            case UnauthorizedException unauthorizedEx:
                problem.Status = (int)HttpStatusCode.Unauthorized;
                problem.Title = "Unauthorized";
                problem.Detail = unauthorizedEx.Message;
                problem.WithReasonCode(unauthorizedEx.ReasonCode);
                break;

            case UnauthorizedAccessException:
                problem.Status = (int)HttpStatusCode.Unauthorized;
                problem.Title = "Unauthorized";
                problem.Detail = "Authentication required";
                problem.WithReasonCode("UNAUTHORIZED");
                break;

            case DbUpdateException dbEx:
                var (status, reasonCode, detail) = MapDatabaseException(dbEx);
                problem.Status = status;
                problem.Title = status == (int)HttpStatusCode.Conflict ? "Conflict" : "Database Error";
                problem.Detail = detail;
                problem.WithReasonCode(reasonCode);
                break;

            default:
                problem.Status = (int)HttpStatusCode.InternalServerError;
                problem.Title = "Internal Server Error";
                problem.Detail = _environment.IsDevelopment()
                    ? exception.ToString()
                    : "An unexpected error occurred";
                problem.WithReasonCode("INTERNAL_ERROR");
                break;
        }

        // Always include correlation ID
        problem.WithCorrelationId(correlationId);

        return problem;
    }

    private static (int StatusCode, string ReasonCode, string Detail) MapDatabaseException(DbUpdateException dbEx)
    {
        // Check for unique constraint violations (PostgreSQL)
        var innerException = dbEx.InnerException;
        if (innerException != null)
        {
            var message = innerException.Message;

            // PostgreSQL unique constraint violation
            if (message.Contains("duplicate key") || message.Contains("unique constraint"))
            {
                // Try to determine the specific constraint
                if (message.Contains("IX_Accounts_Name_Normalized") || message.Contains("Name"))
                {
                    return ((int)HttpStatusCode.Conflict, "DUPLICATE_ACCOUNT_NAME",
                        "An account with this name already exists");
                }

                if (message.Contains("IX_JournalEntries_ExternalId") || message.Contains("ExternalId"))
                {
                    return ((int)HttpStatusCode.Conflict, "DUPLICATE_EXTERNAL_ID",
                        "A journal entry with this external ID already exists");
                }

                return ((int)HttpStatusCode.Conflict, "DUPLICATE_RESOURCE",
                    "A resource with this identifier already exists");
            }
        }

        // Generic database error
        return ((int)HttpStatusCode.InternalServerError, "DATABASE_ERROR",
            "A database error occurred");
    }

    private static string GetProblemType(Exception exception)
    {
        return exception switch
        {
            ValidationException => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            ArgumentException => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            NotFoundException => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            ConflictException => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            UnauthorizedException => "https://tools.ietf.org/html/rfc7235#section-3.1",
            UnauthorizedAccessException => "https://tools.ietf.org/html/rfc7235#section-3.1",
            DbUpdateException => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
        };
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}

