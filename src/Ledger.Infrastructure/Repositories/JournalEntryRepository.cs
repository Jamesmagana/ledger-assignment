using Ledger.Application.Repositories;
using Ledger.Domain.Entities;
using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for JournalEntry operations using EF Core.
/// Ensures atomic transactions for entry + lines.
/// </summary>
public class JournalEntryRepository : IJournalEntryRepository
{
    private readonly LedgerDbContext _context;

    public JournalEntryRepository(LedgerDbContext context)
    {
        _context = context;
    }

    public async Task<JournalEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(je => je.Id == id, cancellationToken);
    }

    public async Task<JournalEntry?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken = default)
    {
        return await _context.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(je => je.ExternalId == externalId, cancellationToken);
    }

    public async Task<(JournalEntry Entry, IReadOnlyList<JournalEntryLine> Lines)> AddWithLinesAsync(
        JournalEntry entry,
        IReadOnlyList<JournalEntryLine> lines,
        CancellationToken cancellationToken = default)
    {
        // Use explicit transaction for atomicity
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Add journal entry
            var entryEntity = _context.JournalEntries.Add(entry);
            await _context.SaveChangesAsync(cancellationToken);

            // Add all lines
            var lineEntities = new List<JournalEntryLine>();
            foreach (var line in lines)
            {
                var lineEntity = _context.JournalEntryLines.Add(line);
                lineEntities.Add(lineEntity.Entity);
            }
            await _context.SaveChangesAsync(cancellationToken);

            // Commit transaction
            await transaction.CommitAsync(cancellationToken);

            return (entryEntity.Entity, lineEntities);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<JournalEntryLine>> GetLinesByJournalEntryIdAsync(
        Guid journalEntryId,
        CancellationToken cancellationToken = default)
    {
        return await _context.JournalEntryLines
            .AsNoTracking()
            .Where(l => l.JournalEntryId == journalEntryId)
            .OrderBy(l => l.AccountId)
            .ThenBy(l => l.Direction)
            .ThenBy(l => l.Amount)
            .ToListAsync(cancellationToken);
    }
}

