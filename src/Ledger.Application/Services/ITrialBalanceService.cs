namespace Ledger.Application.Services;

/// <summary>
/// Service interface for Trial Balance operations.
/// </summary>
public interface ITrialBalanceService
{
    /// <summary>
    /// Gets the trial balance for all accounts.
    /// Validates that total net equals zero.
    /// </summary>
    /// <param name="asOf">Optional date filter. If provided, only includes entries posted on or before this date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trial balance result with all accounts and total net.</returns>
    Task<TrialBalanceResult> GetTrialBalanceAsync(
        DateTime? asOf,
        CancellationToken cancellationToken = default);
}

