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
        // Note: SaveChanges is called by the interceptor or calling code
        // This repository just adds to the context
        await Task.CompletedTask;
    }

    public async Task AddRangeAsync(IEnumerable<AuditLog> auditLogs, CancellationToken cancellationToken = default)
    {
        _context.AuditLogs.AddRange(auditLogs);
        // Note: SaveChanges is called by the interceptor or calling code
        await Task.CompletedTask;
    }
}

