using Ledger.Application.Repositories;
using Ledger.Domain.Entities;

namespace Ledger.Application.Services;

/// <summary>
/// Service implementation for audit logging operations.
/// Handles serialization of old/new values with sensitive field exclusion.
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;

    public AuditLogService(IAuditLogRepository repository)
    {
        _repository = repository;
    }

    public async Task LogEntityChangeAsync(
        string entityName,
        Guid entityId,
        string action,
        object? oldValues,
        object? newValues,
        Guid? performedBy,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        // Serialize old and new values, excluding sensitive fields
        var oldValuesJson = SensitiveFieldExcluder.SerializeToJsonExcludingSensitive(oldValues);
        var newValuesJson = SensitiveFieldExcluder.SerializeToJsonExcludingSensitive(newValues);

        // Create audit log entry
        var auditLog = new AuditLog(
            entityName,
            entityId,
            action,
            oldValuesJson,
            newValuesJson,
            performedBy,
            correlationId);

        // Add to repository (will be saved in same transaction)
        await _repository.AddAsync(auditLog, cancellationToken);
    }
}

