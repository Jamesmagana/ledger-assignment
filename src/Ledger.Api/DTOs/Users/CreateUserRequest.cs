using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.DTOs.Users;

/// <summary>
/// Request DTO for creating a new user.
/// </summary>
public record CreateUserRequest(
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    string Email,

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long.")]
    string Password
);

