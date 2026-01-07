using Ledger.Domain.Enums;

namespace Ledger.Application.Models;

/// <summary>
/// Model representing a single account in the trial balance (Application layer).
/// </summary>
public record TrialBalanceItem(
    Guid AccountId,
    string AccountName,
    AccountType AccountType,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Net
);

