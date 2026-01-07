using System.ComponentModel.DataAnnotations;
using Ledger.Domain.Enums;

namespace Ledger.Api.DTOs.JournalEntries;

/// <summary>
/// Request DTO for a single line in a journal entry.
/// </summary>
public record CreateJournalEntryLineRequest(
    [Required(ErrorMessage = "Account ID is required.")]
    Guid AccountId,
    
    [Required(ErrorMessage = "Direction is required.")]
    LineDirection Direction,
    
    [Required(ErrorMessage = "Amount is required.")]
    [Range(0.0001, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    decimal Amount
);

