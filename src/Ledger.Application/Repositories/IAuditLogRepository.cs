using Ledger.Domain.Entities;

namespace Ledger.Application.Repositories;

/// <summary>
/// Repository interface for AuditLog operations.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Adds a single audit log entry.
    /// </summary>
    Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds multiple audit log entries in a batch.
    /// </summary>
    Task AddRangeAsync(IEnumerable<AuditLog> auditLogs, CancellationToken cancellationToken = default);
}

