using Ledger.Domain.Enums;

namespace Ledger.Api.DTOs.Reports;

/// <summary>
/// Response DTO for a single account in the trial balance.
/// </summary>
public record TrialBalanceItemResponse(
    Guid AccountId,
    string AccountName,
    AccountType AccountType,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Net
);

