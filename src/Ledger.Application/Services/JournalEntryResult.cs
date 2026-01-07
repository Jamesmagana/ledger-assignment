using Ledger.Domain.Entities;

namespace Ledger.Application.Services;

/// <summary>
/// Result of a journal entry operation.
/// </summary>
public record JournalEntryResult(
    JournalEntry Entry,
    IReadOnlyList<JournalEntryLine> Lines,
    bool IsIdempotencyReplay = false
);

