namespace Ledger.Domain.Entities;

/// <summary>
/// Represents an immutable audit log entry.
/// Audit logs are append-only and never modified or deleted.
/// </summary>
public class AuditLog
{
    /// <summary>
    /// Unique identifier for the audit log entry.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Name of the entity that was changed (e.g., "Account", "JournalEntry", "User").
    /// </summary>
    public string EntityName { get; init; } = string.Empty;

    /// <summary>
    /// Unique identifier of the entity that was changed.
    /// </summary>
    public Guid EntityId { get; init; }

    /// <summary>
    /// Action performed (e.g., "CREATE", "UPDATE", "LOGIN").
    /// </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>
    /// JSON representation of the old values (before change).
    /// Null for CREATE actions.
    /// </summary>
    public string? OldValues { get; init; }

    /// <summary>
    /// JSON representation of the new values (after change).
    /// Contains all entity properties except sensitive fields.
    /// </summary>
    public string? NewValues { get; init; }

    /// <summary>
    /// User ID of the user who performed the action.
    /// Nullable for system operations or when user context is unavailable.
    /// </summary>
    public Guid? PerformedBy { get; init; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// Links the audit log to the HTTP request that triggered the change.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the audit log entry was created.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Initializes a new instance of the AuditLog entity.
    /// </summary>
    public AuditLog(
        string entityName,
        Guid entityId,
        string action,
        string? oldValues,
        string? newValues,
        Guid? performedBy,
        string correlationId)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new ArgumentException("Entity name cannot be null or empty.", nameof(entityName));
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Action cannot be null or empty.", nameof(action));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID cannot be null or empty.", nameof(correlationId));
        }

        Id = Guid.NewGuid();
        EntityName = entityName;
        EntityId = entityId;
        Action = action;
        OldValues = oldValues;
        NewValues = newValues;
        PerformedBy = performedBy;
        CorrelationId = correlationId;
        Timestamp = DateTime.UtcNow;
    }
}

