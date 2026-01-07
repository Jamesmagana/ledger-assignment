using Ledger.Application.Models;

namespace Ledger.Application.Services;

/// <summary>
/// Service for computing SHA-256 hash of canonical request payload.
/// </summary>
public interface IRequestHashService
{
    /// <summary>
    /// Computes SHA-256 hash of the canonical request payload.
    /// The hash is deterministic: same input always produces same hash.
    /// </summary>
    string ComputeHash(CreateJournalEntryModel request);
}

