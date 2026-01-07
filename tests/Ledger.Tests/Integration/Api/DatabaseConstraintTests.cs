using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ledger.Api.DTOs.Accounts;
using Ledger.Api.DTOs.Auth;
using Ledger.Api.DTOs.JournalEntries;
using Ledger.Api.DTOs.Users;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ledger.Tests.Integration.Api;

/// <summary>
/// Tests for database constraint violations to ensure database-level enforcement
/// of critical invariants.
/// </summary>
public class DatabaseConstraintTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private string? _testIssuer;
    private string? _testAudience;
    private string? _testSecretKey;

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

        // Test JWT settings
        _testIssuer = "https://ledger-api.test";
        _testAudience = "https://ledger-api.test";
        _testSecretKey = "TestSecretKey_ForIntegrationTests_Minimum32Characters";

        // Create WebApplicationFactory with test database and JWT settings
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove existing DbContext registration
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<LedgerDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add test DbContext
                    var connectionString = _postgresContainer.GetConnectionString();
                    services.AddDbContext<LedgerDbContext>(options =>
                    {
                        options.UseNpgsql(connectionString);
                    });
                });

                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "Jwt:Issuer", _testIssuer },
                        { "Jwt:Audience", _testAudience },
                        { "Jwt:SecretKey", _testSecretKey },
                        { "Jwt:ClockSkewSeconds", "60" }
                    });
                });
            });

        _client = _factory.CreateClient();

        // Get DbContext and run migrations
        using var scope = _factory.Services.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await _context.Database.MigrateAsync();

        // Create a user and get token for authenticated requests
        var email = "constraint@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);
    }

    [Fact]
    public async Task DatabaseConstraint_NegativeAmount_ViolatesCheckConstraint()
    {
        // Arrange
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        var request = new CreateJournalEntryRequest(
            externalId: "EXT-CONSTRAINT-001",
            lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, -100.00m), // Negative amount - should violate constraint
                new(revenueAccount.Id, LineDirection.Credit, 100.00m)
            });

        // Act - Try to insert directly into database (bypassing application validation)
        var line = new Ledger.Domain.Entities.JournalEntryLine(
            Guid.NewGuid(),
            cashAccount.Id,
            LineDirection.Debit,
            -100.00m);

        // Assert - Database constraint should prevent this
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            _context!.JournalEntryLines.Add(line);
            await _context.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task DatabaseConstraint_DuplicateAccountNameCaseInsensitive_ViolatesUniqueIndex()
    {
        // Arrange - Create first account
        await CreateAccountAsync("Cash", AccountType.Asset);

        // Act - Try to create duplicate with different case directly in database
        var duplicateAccount = new Ledger.Domain.Entities.Account("CASH", AccountType.Liability, true);

        // Assert - Database unique index should prevent this
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            _context!.Accounts.Add(duplicateAccount);
            await _context.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task DatabaseConstraint_ZeroAmount_ViolatesCheckConstraint()
    {
        // Arrange
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        // Act - Try to insert line with zero amount directly into database
        var line = new Ledger.Domain.Entities.JournalEntryLine(
            Guid.NewGuid(),
            cashAccount.Id,
            LineDirection.Debit,
            0.00m); // Zero amount - should violate constraint

        // Assert - Database constraint should prevent this
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            _context!.JournalEntryLines.Add(line);
            await _context.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task DatabaseConstraint_DuplicateExternalId_ViolatesUniqueIndex()
    {
        // Arrange - Create first journal entry with external ID
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        var firstRequest = new CreateJournalEntryRequest(
            externalId: "EXT-DUPLICATE-001",
            lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 100.00m),
                new(revenueAccount.Id, LineDirection.Credit, 100.00m)
            });

        await _client!.PostAsJsonAsync("/api/journal-entries", firstRequest);

        // Act - Try to create duplicate external ID directly in database
        var duplicateEntry = new Ledger.Domain.Entities.JournalEntry(
            "EXT-DUPLICATE-001",
            "different-hash",
            DateTime.UtcNow);

        // Assert - Database unique partial index should prevent this
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            _context!.JournalEntries.Add(duplicateEntry);
            await _context.SaveChangesAsync();
        });
    }

    private async Task<AccountResponse> CreateAccountAsync(string name, AccountType type)
    {
        var request = new CreateAccountRequest(name, type, true);
        var response = await _client!.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountResponse>()
            ?? throw new InvalidOperationException("Failed to create account");
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            _client.Dispose();
        }
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }
        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }
}

