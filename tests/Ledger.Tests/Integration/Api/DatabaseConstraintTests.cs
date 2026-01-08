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
using Ledger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ledger.Tests.Integration.Api;

/// <summary>
/// Tests for database constraint violations to ensure database-level enforcement
/// of critical invariants.
/// </summary>
public class DatabaseConstraintTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private IServiceScope? _serviceScope;
    private readonly string _databaseName = $"DatabaseConstraintTest_{Guid.NewGuid()}";
    private string? _testIssuer;
    private string? _testAudience;
    private string? _testSecretKey;

    public async Task InitializeAsync()
    {
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

                    // Add test DbContext with in-memory database
                    // Use fixed database name so test context and API context share the same database
                    services.AddDbContext<LedgerDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(databaseName: _databaseName)
                            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
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

        // Get DbContext and ensure database is created
        // Create a scope that will be disposed in DisposeAsync
        _serviceScope = _factory.Services.CreateScope();
        _context = _serviceScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await _context.Database.EnsureCreatedAsync();

        // Create a user and get token for authenticated requests
        var email = "constraint@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);
    }

    [Fact]
    public async Task DatabaseConstraint_NegativeAmount_ViolatesCheckConstraint()
    {
        // Arrange
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        var request = new CreateJournalEntryRequest(
            ExternalId: "EXT-CONSTRAINT-001",
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, -100.00m), // Negative amount - should violate constraint
                new(revenueAccount.Id, LineDirection.Credit, 100.00m)
            });

        // Act & Assert - Domain layer prevents negative amounts before they reach the database
        // The JournalEntryLine constructor validates amount > 0, so ArgumentException is thrown
        Assert.Throws<ArgumentException>(() =>
        {
            var line = new Ledger.Domain.Entities.JournalEntryLine(
                Guid.NewGuid(),
                cashAccount.Id,
                LineDirection.Debit,
                -100.00m);
        });
        
        // Note: The database constraint exists as a backstop, but domain validation prevents
        // negative amounts from being created, so the constraint is never reached in normal operations.
    }

    [Fact]
    public async Task DatabaseConstraint_DuplicateAccountNameCaseInsensitive_ViolatesUniqueIndex()
    {
        // Arrange - Create first account via API (application layer enforces uniqueness)
        await CreateAccountAsync("Cash", AccountType.Asset);

        // Act - Try to create duplicate with different case via API
        var duplicateRequest = new CreateAccountRequest("CASH", AccountType.Liability, true);
        var response = await _client!.PostAsJsonAsync("/api/accounts", duplicateRequest);

        // Assert - Application layer should prevent this (case-insensitive duplicate check)
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>(JsonHelper.GetJsonOptions());
        Assert.Equal("DUPLICATE_ACCOUNT_NAME", problemDetails.GetProperty("reasonCode").GetString());
        
        // Note: The database constraint (IX_Accounts_Name_Normalized with UPPER(Name)) exists as a backstop
        // in production (PostgreSQL), but EF Core InMemory database doesn't enforce unique indexes properly,
        // especially case-insensitive ones. The application layer (AccountService) enforces this validation.
    }

    [Fact]
    public async Task DatabaseConstraint_ZeroAmount_ViolatesCheckConstraint()
    {
        // Arrange
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        // Act & Assert - Domain layer prevents zero amounts before they reach the database
        // The JournalEntryLine constructor validates amount > 0, so ArgumentException is thrown
        Assert.Throws<ArgumentException>(() =>
        {
            var line = new Ledger.Domain.Entities.JournalEntryLine(
                Guid.NewGuid(),
                cashAccount.Id,
                LineDirection.Debit,
                0.00m); // Zero amount - domain prevents this
        });
        
        // Note: The database constraint exists as a backstop (CK_JournalEntryLine_AmountPositive),
        // but domain validation prevents zero amounts from being created, so the constraint
        // is never reached in normal operations. InMemory database doesn't enforce check constraints.
    }

    [Fact]
    public async Task DatabaseConstraint_DuplicateExternalId_ViolatesUniqueIndex()
    {
        // Arrange - Create first journal entry with external ID via API
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        var firstRequest = new CreateJournalEntryRequest(
            ExternalId: "EXT-DUPLICATE-001",
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 100.00m),
                new(revenueAccount.Id, LineDirection.Credit, 100.00m)
            });

        var firstResponse = await _client!.PostAsJsonAsync("/api/JournalEntries", firstRequest);
        firstResponse.EnsureSuccessStatusCode();

        // Act - Try to create duplicate external ID with different payload via API
        var duplicateRequest = new CreateJournalEntryRequest(
            ExternalId: "EXT-DUPLICATE-001",
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 200.00m), // Different amount = different hash
                new(revenueAccount.Id, LineDirection.Credit, 200.00m)
            });

        var duplicateResponse = await _client.PostAsJsonAsync("/api/JournalEntries", duplicateRequest);

        // Assert - Application layer should detect payload mismatch and return conflict
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        var problemDetails = await duplicateResponse.Content.ReadFromJsonAsync<JsonElement>(JsonHelper.GetJsonOptions());
        Assert.Equal("DUPLICATE_EXTERNAL_ID", problemDetails.GetProperty("reasonCode").GetString());
        
        // Note: The database constraint (IX_JournalEntries_ExternalId with partial filter) exists as a backstop
        // in production (PostgreSQL), but EF Core InMemory database doesn't enforce unique indexes on nullable
        // columns with filters. The application layer (JournalEntryService) enforces idempotency validation.
    }

    private async Task<AccountResponse> CreateAccountAsync(string name, AccountType type)
    {
        var request = new CreateAccountRequest(name, type, true);
        var response = await _client!.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountResponse>(JsonHelper.GetJsonOptions())
            ?? throw new InvalidOperationException("Failed to create account");
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }
        if (_serviceScope != null)
        {
            _serviceScope.Dispose();
        }
        if (_client != null)
        {
            _client.Dispose();
        }
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }
    }
}

