using Ledger.Application.Repositories;
using Ledger.Domain.Entities;
using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for User operations using EF Core.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly LedgerDbContext _context;

    public UserRepository(LedgerDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // Case-insensitive search - compare uppercase versions
        var normalizedEmail = email.ToUpper();
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToUpper() == normalizedEmail, cancellationToken);
    }

    public async Task<User> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        // Attach the entity if not already tracked, then mark as modified
        _context.Users.Attach(user);
        _context.Entry(user).State = EntityState.Modified;

        // Ensure CreatedAt is not modified and UpdatedAt is set
        _context.Entry(user).Property(x => x.CreatedAt).IsModified = false;
        _context.Entry(user).Property(x => x.UpdatedAt).CurrentValue = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }
}

