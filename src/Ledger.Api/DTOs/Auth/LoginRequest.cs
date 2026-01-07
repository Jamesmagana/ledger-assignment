using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.DTOs.Auth;

/// <summary>
/// Request DTO for user login.
/// </summary>
public record LoginRequest(
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    string Email,
    
    [Required(ErrorMessage = "Password is required.")]
    string Password
);

