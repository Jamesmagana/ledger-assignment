using Ledger.Application.Repositories;
using Ledger.Domain.Entities;
using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for AuditLog operations using EF Core.
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly LedgerDbContext _context;

    public AuditLogRepository(LedgerDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        _context.AuditLogs.Add(auditLog);
        // Save changes immediately for manually created audit logs (e.g., LOGIN events)
        // The interceptor skips AuditLog entities to prevent infinite recursion, so this is safe
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<AuditLog> auditLogs, CancellationToken cancellationToken = default)
    {
        _context.AuditLogs.AddRange(auditLogs);
        // Note: SaveChanges is called by the interceptor or calling code
        await Task.CompletedTask;
    }
}

