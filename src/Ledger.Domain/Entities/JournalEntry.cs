namespace Ledger.Domain.Entities;

/// <summary>
/// Represents a journal entry in the double-entry ledger system.
/// 
/// IMMUTABILITY: This entity is IMMUTABLE after posting (when PostedAt is set).
/// Corrections must be implemented via reversing entries only.
/// 
/// IDEMPOTENCY: ExternalId and RequestHash are used for idempotent posting.
/// - Same ExternalId + same RequestHash → replay existing entry (200 OK)
/// - Same ExternalId + different RequestHash → conflict (409)
/// </summary>
public class JournalEntry : BaseEntity
{
    /// <summary>
    /// External identifier for idempotency. Nullable - not all entries require external IDs.
    /// Must be unique at the database level when provided.
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// SHA-256 hash of the canonical request payload.
    /// Used to detect payload mismatches for the same ExternalId.
    /// </summary>
    public string RequestHash { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the journal entry was posted.
    /// Once set, the entry becomes immutable.
    /// </summary>
    public DateTime PostedAt { get; init; }

    /// <summary>
    /// Initializes a new instance of the JournalEntry entity.
    /// </summary>
    /// <param name="externalId">Optional external identifier for idempotency.</param>
    /// <param name="requestHash">SHA-256 hash of the canonical request payload.</param>
    /// <param name="postedAt">UTC timestamp when the entry was posted.</param>
    public JournalEntry(string? externalId, string requestHash, DateTime postedAt)
    {
        if (string.IsNullOrWhiteSpace(requestHash))
        {
            throw new ArgumentException("Request hash cannot be null or empty.", nameof(requestHash));
        }

        ExternalId = externalId;
        RequestHash = requestHash;
        PostedAt = postedAt;
    }
}

