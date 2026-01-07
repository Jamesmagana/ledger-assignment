namespace Ledger.Domain.Enums;

/// <summary>
/// Represents the type of account in the chart of accounts.
/// </summary>
public enum AccountType
{
    /// <summary>
    /// Asset account (e.g., Cash, Accounts Receivable, Inventory)
    /// </summary>
    Asset = 1,

    /// <summary>
    /// Liability account (e.g., Accounts Payable, Loans)
    /// </summary>
    Liability = 2,

    /// <summary>
    /// Equity account (e.g., Capital, Retained Earnings)
    /// </summary>
    Equity = 3,

    /// <summary>
    /// Revenue account (e.g., Sales, Service Revenue)
    /// </summary>
    Revenue = 4,

    /// <summary>
    /// Expense account (e.g., Rent, Salaries, Utilities)
    /// </summary>
    Expense = 5
}

