using Ledger.Application.Models;
using Ledger.Application.Repositories;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Trial Balance operations using EF Core.
/// Uses efficient LEFT JOIN query to include zero-activity accounts.
/// </summary>
public class TrialBalanceRepository : ITrialBalanceRepository
{
    private readonly LedgerDbContext _context;

    public TrialBalanceRepository(LedgerDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TrialBalanceItem>> GetTrialBalanceAsync(
        DateTime? asOf,
        CancellationToken cancellationToken = default)
    {
        // Get all accounts first
        var accounts = await _context.Accounts
            .AsNoTracking()
            .Select(a => new { a.Id, a.Name, a.Type })
            .ToListAsync(cancellationToken);

        // Get journal entry lines filtered by asOf if provided, grouped by account
        var linesQuery = from line in _context.JournalEntryLines
                         join entry in _context.JournalEntries on line.JournalEntryId equals entry.Id
                         where asOf == null || entry.PostedAt <= asOf.Value
                         group new { line.Direction, line.Amount } by line.AccountId into g
                         select new
                         {
                             AccountId = g.Key,
                             TotalDebits = g.Where(x => x.Direction == LineDirection.Debit).Sum(x => x.Amount),
                             TotalCredits = g.Where(x => x.Direction == LineDirection.Credit).Sum(x => x.Amount)
                         };

        var accountBalances = await linesQuery
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Create a dictionary for quick lookup
        var balanceDict = accountBalances.ToDictionary(b => b.AccountId);

        // Build trial balance items - include all accounts (zero-activity accounts will have 0 balances)
        var items = accounts.Select(account =>
        {
            var balance = balanceDict.GetValueOrDefault(account.Id);
            var totalDebits = balance?.TotalDebits ?? 0;
            var totalCredits = balance?.TotalCredits ?? 0;
            return new TrialBalanceItem(
                account.Id,
                account.Name,
                account.Type,
                totalDebits,
                totalCredits,
                totalDebits - totalCredits
            );
        })
        .OrderBy(item => item.AccountName)
        .ToList();

        return items;
    }
}

