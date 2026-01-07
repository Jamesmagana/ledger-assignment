using Ledger.Domain.Entities;

namespace Ledger.Application.Repositories;

/// <summary>
/// Repository interface for Account operations.
/// Implemented in Infrastructure layer.
/// </summary>
public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Account?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<bool> HasJournalLinesAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<Account> AddAsync(Account account, CancellationToken cancellationToken = default);
    Task<Account> UpdateAsync(Account account, CancellationToken cancellationToken = default);
}

