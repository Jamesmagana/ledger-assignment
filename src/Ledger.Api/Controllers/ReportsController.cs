using Ledger.Api.DTOs.Reports;
using Ledger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api.Controllers;

/// <summary>
/// Controller for financial reporting operations.
/// </summary>
[Authorize]
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ITrialBalanceService _trialBalanceService;

    public ReportsController(ITrialBalanceService trialBalanceService)
    {
        _trialBalanceService = trialBalanceService;
    }

    /// <summary>
    /// Gets the trial balance for all accounts.
    /// Includes zero-activity accounts with net=0.
    /// Validates that total net equals zero.
    /// </summary>
    /// <param name="asOf">Optional date filter. If provided, only includes entries posted on or before this date (UTC).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trial balance with all accounts and their net balances.</returns>
    [HttpGet("trial-balance")]
    [ProducesResponseType(typeof(TrialBalanceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TrialBalanceResponse>> GetTrialBalance(
        [FromQuery] DateTime? asOf,
        CancellationToken cancellationToken)
    {
        var result = await _trialBalanceService.GetTrialBalanceAsync(asOf, cancellationToken);

        var response = new TrialBalanceResponse(
            result.AsOf,
            result.Items.Select(item => new TrialBalanceItemResponse(
                item.AccountId,
                item.AccountName,
                item.AccountType,
                item.TotalDebits,
                item.TotalCredits,
                item.Net)).ToList(),
            result.TotalNet);

        return Ok(response);
    }
}

