namespace Ledger.Application.Services;

/// <summary>
/// Service interface for JWT token generation.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Generates a JWT token for a user.
    /// </summary>
    /// <param name="userId">The user's unique identifier.</param>
    /// <param name="email">The user's email address.</param>
    /// <returns>A JWT token string.</returns>
    string GenerateToken(Guid userId, string email);
}

