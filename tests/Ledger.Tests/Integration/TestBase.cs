using Ledger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Ledger.Tests.Integration;

/// <summary>
/// Base class for integration tests using PostgreSQL Testcontainers.
/// </summary>
public abstract class TestBase : IAsyncLifetime
{
    protected PostgreSqlContainer? _postgresContainer;
    protected LedgerDbContext? _context;
    protected IServiceProvider? _serviceProvider;

    public async Task InitializeAsync()
    {
        // Start PostgreSQL container
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("ledger_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();

        await _postgresContainer.StartAsync();

        // Create DbContext
        var connectionString = _postgresContainer.GetConnectionString();
        var services = new ServiceCollection();
        services.AddDbContext<LedgerDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<LedgerDbContext>();

        // Run migrations
        await _context.Database.MigrateAsync();
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

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }
}

