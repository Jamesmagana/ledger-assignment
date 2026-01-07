using Ledger.Domain.Enums;

namespace Ledger.Api.DTOs.Accounts;

/// <summary>
/// Response DTO for account operations.
/// </summary>
public record AccountResponse(
    Guid Id,
    string Name,
    AccountType Type,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

