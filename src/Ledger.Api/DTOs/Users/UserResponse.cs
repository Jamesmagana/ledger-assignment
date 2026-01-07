namespace Ledger.Api.DTOs.Users;

/// <summary>
/// Response DTO for user information.
/// Does not include password hash or sensitive information.
/// </summary>
public class UserResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

