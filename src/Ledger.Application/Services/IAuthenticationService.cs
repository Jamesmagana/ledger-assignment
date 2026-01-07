namespace Ledger.Application.Services;

/// <summary>
/// Service interface for authentication operations.
/// </summary>
public class LoginResult
{
    public string Token { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// Service interface for authentication operations.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Authenticates a user and returns a JWT token.
    /// </summary>
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
}

