using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.DTOs.Accounts;

/// <summary>
/// Request DTO for updating an account.
/// Only IsActive can be updated; Type is immutable after account usage.
/// </summary>
public record UpdateAccountRequest(
    [Required(ErrorMessage = "IsActive flag is required.")]
    bool IsActive
);

