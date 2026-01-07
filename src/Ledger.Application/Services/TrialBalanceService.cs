using Ledger.Application.Exceptions;
using Ledger.Application.Repositories;

namespace Ledger.Application.Services;

/// <summary>
/// Service implementation for Trial Balance operations.
/// Validates that total net equals zero (double-entry accounting requirement).
/// </summary>
public class TrialBalanceService : ITrialBalanceService
{
    private readonly ITrialBalanceRepository _repository;

    public TrialBalanceService(ITrialBalanceRepository repository)
    {
        _repository = repository;
    }

    public async Task<TrialBalanceResult> GetTrialBalanceAsync(
        DateTime? asOf,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetTrialBalanceAsync(asOf, cancellationToken);

        // Calculate total net across all accounts
        var totalNet = items.Sum(item => item.Net);

        // Validate that total net equals zero (double-entry accounting requirement)
        if (totalNet != 0)
        {
            throw new ValidationException(
                $"Trial balance is unbalanced. Total net: {totalNet}. This indicates a data integrity issue.",
                "UNBALANCED_TRIAL_BALANCE");
        }

        return new TrialBalanceResult(asOf, items, totalNet);
    }
}

