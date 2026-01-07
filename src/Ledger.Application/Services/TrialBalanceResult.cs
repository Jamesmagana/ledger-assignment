using Ledger.Application.Models;

namespace Ledger.Application.Services;

/// <summary>
/// Result of a trial balance query.
/// </summary>
public record TrialBalanceResult(
    DateTime? AsOf,
    IReadOnlyList<TrialBalanceItem> Items,
    decimal TotalNet
);

