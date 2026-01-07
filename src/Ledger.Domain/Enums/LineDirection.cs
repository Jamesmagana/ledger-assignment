namespace Ledger.Domain.Enums;

/// <summary>
/// Represents the direction of a journal entry line (Debit or Credit).
/// A line MUST be either Debit OR Credit, never both.
/// </summary>
public enum LineDirection
{
    /// <summary>
    /// Debit entry (increases assets/expenses, decreases liabilities/equity/revenue)
    /// </summary>
    Debit = 1,

    /// <summary>
    /// Credit entry (increases liabilities/equity/revenue, decreases assets/expenses)
    /// </summary>
    Credit = 2
}

