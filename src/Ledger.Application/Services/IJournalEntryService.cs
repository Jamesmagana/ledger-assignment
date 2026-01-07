using Ledger.Application.Models;

namespace Ledger.Application.Services;

/// <summary>
/// Service interface for JournalEntry business operations.
/// </summary>
public interface IJournalEntryService
{
    Task<JournalEntryResult> PostJournalEntryAsync(
        CreateJournalEntryModel request,
        CancellationToken cancellationToken = default);
    
    Task<JournalEntryResult?> GetJournalEntryByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

