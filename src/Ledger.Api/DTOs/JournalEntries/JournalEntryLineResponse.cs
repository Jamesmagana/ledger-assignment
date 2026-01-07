using Ledger.Domain.Enums;

namespace Ledger.Api.DTOs.JournalEntries;

/// <summary>
/// Response DTO for a journal entry line.
/// </summary>
public record JournalEntryLineResponse(
    Guid Id,
    Guid AccountId,
    LineDirection Direction,
    decimal Amount
);

