using Ledger.Domain.Enums;

namespace Ledger.Application.Models;

/// <summary>
/// Model for creating a journal entry line (Application layer).
/// </summary>
public record CreateJournalEntryLineModel(
    Guid AccountId,
    LineDirection Direction,
    decimal Amount
);

