namespace Ledger.Application.Services;

/// <summary>
/// Service interface for audit logging operations.
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Logs an entity change to the audit log.
    /// </summary>
    /// <param name="entityName">Name of the entity (e.g., "Account", "JournalEntry").</param>
    /// <param name="entityId">Unique identifier of the entity.</param>
    /// <param name="action">Action performed (e.g., "CREATE", "UPDATE", "LOGIN").</param>
    /// <param name="oldValues">Old values before change (null for CREATE).</param>
    /// <param name="newValues">New values after change.</param>
    /// <param name="performedBy">User ID who performed the action (nullable).</param>
    /// <param name="correlationId">Correlation ID for request tracing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogEntityChangeAsync(
        string entityName,
        Guid entityId,
        string action,
        object? oldValues,
        object? newValues,
        Guid? performedBy,
        string correlationId,
        CancellationToken cancellationToken = default);
}

