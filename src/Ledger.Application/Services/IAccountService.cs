using Ledger.Domain.Entities;
using Ledger.Domain.Enums;

namespace Ledger.Application.Services;

/// <summary>
/// Service interface for Account business operations.
/// </summary>
public interface IAccountService
{
    Task<Account> CreateAccountAsync(string name, AccountType type, bool isActive, CancellationToken cancellationToken = default);
    Task<Account?> GetAccountByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> GetAllAccountsAsync(CancellationToken cancellationToken = default);
    Task<Account> UpdateAccountIsActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
}

