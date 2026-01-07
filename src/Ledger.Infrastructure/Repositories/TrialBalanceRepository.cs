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
        // Use LINQ with LEFT JOIN to include all accounts (even zero-activity)
        // First, get all accounts
        var accountsQuery = _context.Accounts.AsQueryable();

        // Get journal entry lines filtered by asOf if provided
        var linesQuery = from line in _context.JournalEntryLines
                         join entry in _context.JournalEntries on line.JournalEntryId equals entry.Id
                         where asOf == null || entry.PostedAt <= asOf
                         select line;

        // Join accounts with lines (LEFT JOIN)
        var query = from account in accountsQuery
                    join line in linesQuery on account.Id equals line.AccountId into accountLines
                    from line in accountLines.DefaultIfEmpty()
                    group new { line, account } by new { account.Id, account.Name, account.Type } into g
                    select new TrialBalanceItem(
                        g.Key.Id,
                        g.Key.Name,
                        g.Key.Type,
                        g.Sum(x => x.line != null && x.line.Direction == LineDirection.Debit ? x.line.Amount : 0),
                        g.Sum(x => x.line != null && x.line.Direction == LineDirection.Credit ? x.line.Amount : 0),
                        g.Sum(x => x.line != null && x.line.Direction == LineDirection.Debit ? x.line.Amount : 0) -
                        g.Sum(x => x.line != null && x.line.Direction == LineDirection.Credit ? x.line.Amount : 0)
                    );

        var items = await query
            .OrderBy(item => item.AccountName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return items;
    }
}

