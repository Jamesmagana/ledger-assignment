namespace Ledger.Api.DTOs.Auth;

/// <summary>
/// Response DTO for successful login.
/// Contains the JWT token and user information.
/// </summary>
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
}

