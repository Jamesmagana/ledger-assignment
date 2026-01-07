using Ledger.Domain.Enums;

namespace Ledger.Domain.Entities;

/// <summary>
/// Represents a single line in a journal entry.
/// 
/// IMMUTABILITY: Lines are immutable once created as part of an immutable JournalEntry.
/// 
/// DOMAIN INVARIANTS:
/// - Amount MUST be greater than zero
/// - Direction MUST be either Debit OR Credit (never both)
/// - All lines belong to a single JournalEntry
/// - All referenced accounts MUST exist and be active (enforced in Application layer)
/// </summary>
public class JournalEntryLine
{
    /// <summary>
    /// Unique identifier for the line.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Foreign key to the parent JournalEntry.
    /// </summary>
    public Guid JournalEntryId { get; init; }

    /// <summary>
    /// Foreign key to the Account referenced by this line.
    /// </summary>
    public Guid AccountId { get; init; }

    /// <summary>
    /// The direction of the line (Debit or Credit).
    /// A line MUST be either Debit OR Credit, never both.
    /// </summary>
    public LineDirection Direction { get; init; }

    /// <summary>
    /// The amount of the line. Must be greater than zero.
    /// Precision: numeric(20,4) in database.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// Initializes a new instance of the JournalEntryLine entity.
    /// </summary>
    /// <param name="journalEntryId">The ID of the parent journal entry.</param>
    /// <param name="accountId">The ID of the account.</param>
    /// <param name="direction">The direction (Debit or Credit).</param>
    /// <param name="amount">The amount. Must be greater than zero.</param>
    /// <exception cref="ArgumentException">Thrown when amount is less than or equal to zero.</exception>
    public JournalEntryLine(Guid journalEntryId, Guid accountId, LineDirection direction, decimal amount)
    {
        if (journalEntryId == Guid.Empty)
        {
            throw new ArgumentException("Journal entry ID cannot be empty.", nameof(journalEntryId));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account ID cannot be empty.", nameof(accountId));
        }

        if (amount <= 0)
        {
            throw new ArgumentException("Amount must be greater than zero.", nameof(amount));
        }

        JournalEntryId = journalEntryId;
        AccountId = accountId;
        Direction = direction;
        Amount = amount;
    }
}

