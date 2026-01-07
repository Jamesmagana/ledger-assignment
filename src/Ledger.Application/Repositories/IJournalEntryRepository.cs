using Ledger.Domain.Entities;

namespace Ledger.Application.Repositories;

/// <summary>
/// Repository interface for JournalEntry operations.
/// Implemented in Infrastructure layer.
/// </summary>
public interface IJournalEntryRepository
{
    Task<JournalEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<JournalEntry?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken = default);
    Task<(JournalEntry Entry, IReadOnlyList<JournalEntryLine> Lines)> AddWithLinesAsync(
        JournalEntry entry,
        IReadOnlyList<JournalEntryLine> lines,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JournalEntryLine>> GetLinesByJournalEntryIdAsync(
        Guid journalEntryId,
        CancellationToken cancellationToken = default);
}

