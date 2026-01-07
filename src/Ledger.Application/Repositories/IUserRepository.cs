using Ledger.Domain.Entities;

namespace Ledger.Application.Repositories;

/// <summary>
/// Repository interface for User operations.
/// Implemented in Infrastructure layer.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User> AddAsync(User user, CancellationToken cancellationToken = default);
    Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);
}

