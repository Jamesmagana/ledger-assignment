namespace Ledger.Api.DTOs.JournalEntries;

/// <summary>
/// Response DTO for a journal entry.
/// </summary>
public record JournalEntryResponse(
    Guid Id,
    string? ExternalId,
    DateTime PostedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<JournalEntryLineResponse> Lines,
    bool IdempotencyReplay = false
);

