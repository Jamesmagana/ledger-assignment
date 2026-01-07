using Ledger.Application.Models;

namespace Ledger.Application.Repositories;

/// <summary>
/// Repository interface for Trial Balance operations.
/// Implemented in Infrastructure layer.
/// </summary>
public interface ITrialBalanceRepository
{
    /// <summary>
    /// Gets the trial balance for all accounts.
    /// Includes zero-activity accounts with net=0.
    /// </summary>
    /// <param name="asOf">Optional date filter. If provided, only includes entries posted on or before this date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of trial balance items, one per account.</returns>
    Task<IReadOnlyList<TrialBalanceItem>> GetTrialBalanceAsync(
        DateTime? asOf,
        CancellationToken cancellationToken = default);
}

