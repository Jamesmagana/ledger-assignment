using Ledger.Api.DTOs.Accounts;
using Ledger.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api.Controllers;

/// <summary>
/// Controller for Account management operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly IAccountService _accountService;

    public AccountsController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    /// <summary>
    /// Creates a new account.
    /// </summary>
    /// <param name="request">Account creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created account.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountResponse>> CreateAccount(
        [FromBody] CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _accountService.CreateAccountAsync(
            request.Name,
            request.Type,
            request.IsActive,
            cancellationToken);

        var response = new AccountResponse(
            account.Id,
            account.Name,
            account.Type,
            account.IsActive,
            account.CreatedAt,
            account.UpdatedAt);

        return CreatedAtAction(
            nameof(GetAccountById),
            new { id = account.Id },
            response);
    }

    /// <summary>
    /// Gets all accounts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all accounts.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AccountResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AccountResponse>>> GetAllAccounts(
        CancellationToken cancellationToken)
    {
        var accounts = await _accountService.GetAllAccountsAsync(cancellationToken);

        var responses = accounts.Select(a => new AccountResponse(
            a.Id,
            a.Name,
            a.Type,
            a.IsActive,
            a.CreatedAt,
            a.UpdatedAt));

        return Ok(responses);
    }

    /// <summary>
    /// Gets an account by ID.
    /// </summary>
    /// <param name="id">Account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Account if found.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> GetAccountById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var account = await _accountService.GetAccountByIdAsync(id, cancellationToken);

        if (account == null)
        {
            return NotFound();
        }

        var response = new AccountResponse(
            account.Id,
            account.Name,
            account.Type,
            account.IsActive,
            account.CreatedAt,
            account.UpdatedAt);

        return Ok(response);
    }

    /// <summary>
    /// Updates an account's IsActive status.
    /// Account type cannot be changed after the account has been used in journal entries.
    /// </summary>
    /// <param name="id">Account ID.</param>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated account.</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> UpdateAccount(
        [FromRoute] Guid id,
        [FromBody] UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await _accountService.UpdateAccountIsActiveAsync(
            id,
            request.IsActive,
            cancellationToken);

        var response = new AccountResponse(
            account.Id,
            account.Name,
            account.Type,
            account.IsActive,
            account.CreatedAt,
            account.UpdatedAt);

        return Ok(response);
    }
}

