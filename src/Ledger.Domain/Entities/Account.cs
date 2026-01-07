using Ledger.Domain.Enums;

namespace Ledger.Domain.Entities;

/// <summary>
/// Represents an account in the chart of accounts.
/// Account names must be unique case-insensitively.
/// Account type cannot change after first usage in a journal entry.
/// </summary>
public class Account : BaseEntity
{
    /// <summary>
    /// The name of the account. Must be unique case-insensitively.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The type of account (Asset, Liability, Equity, Revenue, Expense).
    /// Immutable after creation if the account has been used in any journal entry.
    /// </summary>
    public AccountType Type { get; init; }

    /// <summary>
    /// Indicates whether the account is active and can be used in journal entries.
    /// Can be toggled to disable an account without deleting it.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Initializes a new instance of the Account entity.
    /// </summary>
    /// <param name="name">The account name.</param>
    /// <param name="type">The account type.</param>
    /// <param name="isActive">Whether the account is active (default: true).</param>
    public Account(string name, AccountType type, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Account name cannot be null or empty.", nameof(name));
        }

        Name = name;
        Type = type;
        IsActive = isActive;
    }

    /// <summary>
    /// Creates a new account with the same properties but updated IsActive state.
    /// Used for toggling account activation without modifying the original entity.
    /// Note: This method creates a new instance with the same base properties.
    /// The Id, CreatedAt, and UpdatedAt are preserved via object initializer.
    /// </summary>
    public Account WithIsActive(bool isActive)
    {
        return new Account(Name, Type, isActive)
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
        };
    }
}

