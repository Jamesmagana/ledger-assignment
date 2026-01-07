namespace Ledger.Application.Models;

/// <summary>
/// Model for creating a journal entry (Application layer).
/// </summary>
public record CreateJournalEntryModel(
    string? ExternalId,
    IReadOnlyList<CreateJournalEntryLineModel> Lines
);

