using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Tests.Integration;

/// <summary>
/// Base class for integration tests using in-memory database.
/// </summary>
public abstract class TestBase : IAsyncLifetime
{
    protected LedgerDbContext? _context;
    protected IServiceProvider? _serviceProvider;

    public async Task InitializeAsync()
    {
        // Create DbContext with in-memory database
        var services = new ServiceCollection();
        services.AddDbContext<LedgerDbContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName: $"TestBase_{Guid.NewGuid()}");
        });

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<LedgerDbContext>();

        // Ensure database is created
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

