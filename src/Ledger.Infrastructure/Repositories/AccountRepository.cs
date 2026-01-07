using Ledger.Application.Repositories;
using Ledger.Domain.Entities;
using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Account operations using EF Core.
/// </summary>
public class AccountRepository : IAccountRepository
{
    private readonly LedgerDbContext _context;

    public AccountRepository(LedgerDbContext context)
    {
        _context = context;
    }

    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Accounts
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Account?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        // Case-insensitive search - compare uppercase versions
        var normalizedName = name.ToUpper();
        return await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Name.ToUpper() == normalizedName,
                cancellationToken);
    }

    public async Task<bool> HasJournalLinesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _context.JournalEntryLines
            .AnyAsync(l => l.AccountId == accountId, cancellationToken);
    }

    public async Task<Account> AddAsync(Account account, CancellationToken cancellationToken = default)
    {
        var entry = _context.Accounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);
        return entry.Entity;
    }

    public async Task<Account> UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        var entry = _context.Accounts.Update(account);

        // Set UpdatedAt to current UTC time
        entry.Property(nameof(account.UpdatedAt)).CurrentValue = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Reload to get the updated entity with correct UpdatedAt
        await entry.ReloadAsync(cancellationToken);

        return entry.Entity;
    }
}

