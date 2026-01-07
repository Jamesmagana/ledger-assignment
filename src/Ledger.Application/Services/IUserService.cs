using Ledger.Domain.Entities;

namespace Ledger.Application.Services;

/// <summary>
/// Service interface for User business operations.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Creates a new user with email and password.
    /// </summary>
    Task<User> CreateUserAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a user with email and password.
    /// Returns the user if authentication succeeds, null otherwise.
    /// Updates FailedLoginAttempts and LastLoginAt.
    /// </summary>
    Task<User?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default);
}

