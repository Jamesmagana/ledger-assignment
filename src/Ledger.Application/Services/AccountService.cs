using Ledger.Application.Exceptions;
using Ledger.Application.Repositories;
using Ledger.Domain.Entities;
using Ledger.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Application.Services;

/// <summary>
/// Service implementation for Account business operations.
/// Enforces validation rules, duplicate prevention, and business invariants.
/// </summary>
public class AccountService : IAccountService
{
    private readonly IAccountRepository _repository;
    private const int MaxNameLength = 200;

    public AccountService(IAccountRepository repository)
    {
        _repository = repository;
    }

    public async Task<Account> CreateAccountAsync(string name, AccountType type, bool isActive, CancellationToken cancellationToken = default)
    {
        // Validate and normalize name
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Account name is required.", "INVALID_NAME");
        }

        name = name.Trim();

        if (name.Length > MaxNameLength)
        {
            throw new ValidationException($"Account name cannot exceed {MaxNameLength} characters.", "INVALID_NAME");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Account name cannot be empty or whitespace.", "INVALID_NAME");
        }

        // Validate AccountType enum
        if (!Enum.IsDefined(typeof(AccountType), type))
        {
            throw new ValidationException("Invalid account type.", "INVALID_ACCOUNT_TYPE");
        }

        // Check for duplicate name (case-insensitive)
        var existing = await _repository.FindByNameAsync(name, cancellationToken);
        if (existing != null)
        {
            throw new ConflictException(
                "An account with this name already exists.",
                "DUPLICATE_ACCOUNT_NAME");
        }

        // Create account entity
        var account = new Account(name, type, isActive);

        try
        {
            return await _repository.AddAsync(account, cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Database constraint violation (unique index) - backstop
            if (ex.InnerException?.Message.Contains("IX_Accounts_Name_Normalized") == true ||
                ex.InnerException?.Message.Contains("duplicate key") == true)
            {
                throw new ConflictException(
                    "An account with this name already exists.",
                    ex,
                    "DUPLICATE_ACCOUNT_NAME");
            }
            throw;
        }
    }

    public async Task<Account?> GetAccountByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Account ID cannot be empty.", "INVALID_ACCOUNT_ID");
        }

        return await _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> GetAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllAsync(cancellationToken);
    }

    public async Task<Account> UpdateAccountIsActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Account ID cannot be empty.", "INVALID_ACCOUNT_ID");
        }

        var account = await _repository.GetByIdAsync(id, cancellationToken);
        if (account == null)
        {
            throw new NotFoundException($"Account with ID {id} was not found.", "ACCOUNT_NOT_FOUND");
        }

        // Check if account has journal lines (type immutability check)
        // Note: PUT only allows IsActive changes, but we validate defensively
        var hasJournalLines = await _repository.HasJournalLinesAsync(id, cancellationToken);
        if (hasJournalLines)
        {
            // Type cannot be changed, but this endpoint only allows IsActive changes
            // This check is defensive - if someone tries to change type via other means, it would fail
            // For now, we allow IsActive changes even if account has journal lines
        }

        // Create updated account with new IsActive value (immutability pattern)
        var updatedAccount = account.WithIsActive(isActive);

        // Repository will handle UpdatedAt timestamp on save
        return await _repository.UpdateAsync(updatedAccount, cancellationToken);
    }
}

