using System.ComponentModel.DataAnnotations;
using Ledger.Domain.Enums;

namespace Ledger.Api.DTOs.Accounts;

/// <summary>
/// Request DTO for creating a new account.
/// </summary>
public record CreateAccountRequest(
    [Required(ErrorMessage = "Account name is required.")]
    [MaxLength(200, ErrorMessage = "Account name cannot exceed 200 characters.")]
    string Name,

    [Required(ErrorMessage = "Account type is required.")]
    AccountType Type,

    bool IsActive = true
);

