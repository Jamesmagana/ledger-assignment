namespace Ledger.Api.DTOs.Reports;

/// <summary>
/// Response DTO for the trial balance report.
/// </summary>
public record TrialBalanceResponse(
    DateTime? AsOf,
    IReadOnlyList<TrialBalanceItemResponse> Items,
    decimal TotalNet
);

