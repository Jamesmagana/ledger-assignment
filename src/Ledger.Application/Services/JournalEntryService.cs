using Ledger.Application.Exceptions;
using Ledger.Application.Models;
using Ledger.Application.Repositories;
using Ledger.Domain.Entities;
using Ledger.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Application.Services;

/// <summary>
/// Service implementation for JournalEntry business operations.
/// Enforces double-entry accounting rules, validation, and idempotency.
/// </summary>
public class JournalEntryService : IJournalEntryService
{
    private readonly IJournalEntryRepository _journalEntryRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IRequestHashService _hashService;
    private const int MaxDecimalPlaces = 4;

    public JournalEntryService(
        IJournalEntryRepository journalEntryRepository,
        IAccountRepository accountRepository,
        IRequestHashService hashService)
    {
        _journalEntryRepository = journalEntryRepository;
        _accountRepository = accountRepository;
        _hashService = hashService;
    }

    public async Task<JournalEntryResult> PostJournalEntryAsync(
        CreateJournalEntryModel request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate line count
        if (request.Lines == null || request.Lines.Count < 2)
        {
            throw new ValidationException(
                "Journal entry must have at least 2 lines.",
                "INVALID_LINE_COUNT");
        }

        // 2. Validate and normalize lines
        var validatedLines = new List<(Guid AccountId, LineDirection Direction, decimal Amount)>();
        var accountIds = new HashSet<Guid>();

        foreach (var line in request.Lines)
        {
            // Validate amount > 0
            if (line.Amount <= 0)
            {
                throw new ValidationException(
                    $"Line amount must be greater than zero. Amount: {line.Amount}",
                    "INVALID_AMOUNT");
            }

            // Validate decimal scale <= 4
            var scale = GetDecimalScale(line.Amount);
            if (scale > MaxDecimalPlaces)
            {
                throw new ValidationException(
                    $"Line amount cannot have more than {MaxDecimalPlaces} decimal places. Amount: {line.Amount}",
                    "INVALID_AMOUNT_SCALE");
            }

            // Validate direction is valid enum
            if (!Enum.IsDefined(typeof(LineDirection), line.Direction))
            {
                throw new ValidationException(
                    $"Invalid line direction: {line.Direction}",
                    "INVALID_DIRECTION");
            }

            validatedLines.Add((line.AccountId, line.Direction, line.Amount));
            accountIds.Add(line.AccountId);
        }

        // 3. Validate all accounts exist and are active
        foreach (var accountId in accountIds)
        {
            var account = await _accountRepository.GetByIdAsync(accountId, cancellationToken);
            if (account == null)
            {
                throw new NotFoundException(
                    $"Account with ID {accountId} was not found.",
                    "ACCOUNT_NOT_FOUND");
            }

            if (!account.IsActive)
            {
                throw new ValidationException(
                    $"Account with ID {accountId} is not active.",
                    "ACCOUNT_INACTIVE");
            }
        }

        // 4. Validate balance (debits == credits)
        var totalDebits = validatedLines
            .Where(l => l.Direction == LineDirection.Debit)
            .Sum(l => l.Amount);

        var totalCredits = validatedLines
            .Where(l => l.Direction == LineDirection.Credit)
            .Sum(l => l.Amount);

        if (totalDebits != totalCredits)
        {
            throw new ValidationException(
                $"Journal entry is unbalanced. Total Debits: {totalDebits}, Total Credits: {totalCredits}",
                "UNBALANCED_ENTRY");
        }

        // 5. Check idempotency (if ExternalId provided)
        if (!string.IsNullOrWhiteSpace(request.ExternalId))
        {
            var existing = await _journalEntryRepository.FindByExternalIdAsync(
                request.ExternalId,
                cancellationToken);

            if (existing != null)
            {
                // Compute hash of current request
                var computedHash = _hashService.ComputeHash(request);

                // Compare hashes
                if (existing.RequestHash == computedHash)
                {
                    // Idempotent replay - return existing entry
                    var existingLines = await _journalEntryRepository.GetLinesByJournalEntryIdAsync(
                        existing.Id,
                        cancellationToken);

                    return new JournalEntryResult(existing, existingLines, IsIdempotencyReplay: true);
                }
                else
                {
                    // Payload mismatch - conflict
                    throw new ConflictException(
                        "A journal entry with this external ID already exists with a different payload.",
                        "DUPLICATE_EXTERNAL_ID");
                }
            }
        }

        // 6. Create journal entry and lines
        var requestHash = _hashService.ComputeHash(request);
        var postedAt = DateTime.UtcNow;

        // Generate ID for journal entry before creating lines
        // This ensures the ID is available when creating JournalEntryLine instances
        var journalEntryId = Guid.NewGuid();

        var journalEntry = new JournalEntry(
            request.ExternalId,
            requestHash,
            postedAt)
        {
            Id = journalEntryId
        };

        var journalEntryLines = validatedLines.Select(line =>
            new JournalEntryLine(
                journalEntryId,
                line.AccountId,
                line.Direction,
                line.Amount)
        ).ToList();

        try
        {
            var (entry, lines) = await _journalEntryRepository.AddWithLinesAsync(
                journalEntry,
                journalEntryLines,
                cancellationToken);

            return new JournalEntryResult(entry, lines, IsIdempotencyReplay: false);
        }
        catch (DbUpdateException ex)
        {
            // Handle unique constraint violation (concurrent insert with same ExternalId)
            if (ex.InnerException?.Message.Contains("IX_JournalEntries_ExternalId") == true ||
                ex.InnerException?.Message.Contains("duplicate key") == true)
            {
                // Fetch existing entry and compare hash
                if (!string.IsNullOrWhiteSpace(request.ExternalId))
                {
                    var existing = await _journalEntryRepository.FindByExternalIdAsync(
                        request.ExternalId,
                        cancellationToken);

                    if (existing != null)
                    {
                        var computedHash = _hashService.ComputeHash(request);
                        if (existing.RequestHash == computedHash)
                        {
                            // Idempotent replay
                            var existingLines = await _journalEntryRepository.GetLinesByJournalEntryIdAsync(
                                existing.Id,
                                cancellationToken);

                            return new JournalEntryResult(existing, existingLines, IsIdempotencyReplay: true);
                        }
                        else
                        {
                            // Payload mismatch
                            throw new ConflictException(
                                "A journal entry with this external ID already exists with a different payload.",
                                ex,
                                "DUPLICATE_EXTERNAL_ID");
                        }
                    }
                }
            }
            throw;
        }
    }

    public async Task<JournalEntryResult?> GetJournalEntryByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("Journal entry ID cannot be empty.", "INVALID_JOURNAL_ENTRY_ID");
        }

        var entry = await _journalEntryRepository.GetByIdAsync(id, cancellationToken);
        if (entry == null)
        {
            return null;
        }

        var lines = await _journalEntryRepository.GetLinesByJournalEntryIdAsync(id, cancellationToken);
        return new JournalEntryResult(entry, lines, IsIdempotencyReplay: false);
    }

    private static int GetDecimalScale(decimal value)
    {
        var bits = decimal.GetBits(value);
        var scale = (bits[3] >> 16) & 0x7F;
        return scale;
    }
}

