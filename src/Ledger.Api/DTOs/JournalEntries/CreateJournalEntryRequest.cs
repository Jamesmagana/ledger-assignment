using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.DTOs.JournalEntries;

/// <summary>
/// Request DTO for creating a journal entry.
/// </summary>
public record CreateJournalEntryRequest(
    string? ExternalId,
    
    [Required(ErrorMessage = "Lines are required.")]
    [MinLength(2, ErrorMessage = "Journal entry must have at least 2 lines.")]
    IReadOnlyList<CreateJournalEntryLineRequest> Lines
);

