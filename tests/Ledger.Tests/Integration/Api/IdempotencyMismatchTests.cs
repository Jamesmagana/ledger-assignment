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
/// Tests for idempotency mismatch scenarios where same externalId
/// is used with different payloads.
/// </summary>
public class IdempotencyMismatchTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private IServiceScope? _serviceScope;
    private readonly string _databaseName = $"IdempotencyMismatchTest_{Guid.NewGuid()}";
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

        // Create a user and get token
        var email = "idempotency@example.com";
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
    public async Task PostJournalEntry_SameExternalId_DifferentAmount_Returns409Conflict()
    {
        // Arrange
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        var externalId = "EXT-MISMATCH-001";

        // First request
        var firstRequest = new CreateJournalEntryRequest(
            ExternalId: externalId,
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 1000.00m),
                new(revenueAccount.Id, LineDirection.Credit, 1000.00m)
            });

        var firstResponse = await _client!.PostAsJsonAsync("/api/JournalEntries", firstRequest);
        firstResponse.EnsureSuccessStatusCode();

        // Act - Second request with same externalId but different amount
        var secondRequest = new CreateJournalEntryRequest(
            ExternalId: externalId,
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 2000.00m), // Different amount
                new(revenueAccount.Id, LineDirection.Credit, 2000.00m)
            });

        var secondResponse = await _client.PostAsJsonAsync("/api/JournalEntries", secondRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var problemDetails = await JsonHelper.ReadFromJsonAsync<JsonElement>(secondResponse.Content);
        Assert.Equal("DUPLICATE_EXTERNAL_ID", problemDetails.GetProperty("reasonCode").GetString());
        Assert.NotNull(problemDetails.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task PostJournalEntry_SameExternalId_DifferentAccounts_Returns409Conflict()
    {
        // Arrange
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);
        var expenseAccount = await CreateAccountAsync("Expense", AccountType.Expense);

        var externalId = "EXT-MISMATCH-002";

        // First request
        var firstRequest = new CreateJournalEntryRequest(
            ExternalId: externalId,
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 1000.00m),
                new(revenueAccount.Id, LineDirection.Credit, 1000.00m)
            });

        var firstResponse = await _client!.PostAsJsonAsync("/api/JournalEntries", firstRequest);
        firstResponse.EnsureSuccessStatusCode();

        // Act - Second request with same externalId but different accounts
        var secondRequest = new CreateJournalEntryRequest(
            ExternalId: externalId,
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 1000.00m),
                new(expenseAccount.Id, LineDirection.Credit, 1000.00m) // Different account
            });

        var secondResponse = await _client.PostAsJsonAsync("/api/JournalEntries", secondRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var problemDetails = await JsonHelper.ReadFromJsonAsync<JsonElement>(secondResponse.Content);
        Assert.Equal("DUPLICATE_EXTERNAL_ID", problemDetails.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task PostJournalEntry_SameExternalId_DifferentLineCount_Returns409Conflict()
    {
        // Arrange
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);
        var expenseAccount = await CreateAccountAsync("Expense", AccountType.Expense);

        var externalId = "EXT-MISMATCH-003";

        // First request with 2 lines
        var firstRequest = new CreateJournalEntryRequest(
            ExternalId: externalId,
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 1000.00m),
                new(revenueAccount.Id, LineDirection.Credit, 1000.00m)
            });

        var firstResponse = await _client!.PostAsJsonAsync("/api/JournalEntries", firstRequest);
        firstResponse.EnsureSuccessStatusCode();

        // Act - Second request with same externalId but 3 lines (must be balanced)
        var secondRequest = new CreateJournalEntryRequest(
            ExternalId: externalId,
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 500.00m),
                new(revenueAccount.Id, LineDirection.Credit, 500.00m),
                new(expenseAccount.Id, LineDirection.Debit, 500.00m), // Extra line - must be debit to balance
                new(expenseAccount.Id, LineDirection.Credit, 500.00m) // Counter-balance
            });

        var secondResponse = await _client.PostAsJsonAsync("/api/JournalEntries", secondRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var problemDetails = await JsonHelper.ReadFromJsonAsync<JsonElement>(secondResponse.Content);
        Assert.Equal("DUPLICATE_EXTERNAL_ID", problemDetails.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task PostJournalEntry_SameExternalId_SamePayload_Returns200OkReplay()
    {
        // Arrange
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        var externalId = "EXT-REPLAY-001";
        var lines = new List<CreateJournalEntryLineRequest>
        {
            new(cashAccount.Id, LineDirection.Debit, 1000.00m),
            new(revenueAccount.Id, LineDirection.Credit, 1000.00m)
        };

        // First request
        var firstRequest = new CreateJournalEntryRequest(externalId, lines);
        var firstResponse = await _client!.PostAsJsonAsync("/api/JournalEntries", firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstEntry = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(firstResponse.Content);
        Assert.NotNull(firstEntry);

        // Act - Second request with identical payload
        var secondRequest = new CreateJournalEntryRequest(externalId, lines);
        var secondResponse = await _client.PostAsJsonAsync("/api/JournalEntries", secondRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondEntry = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(secondResponse.Content);
        Assert.NotNull(secondEntry);
        Assert.Equal(firstEntry.Id, secondEntry.Id); // Same entry
        Assert.True(secondEntry.IdempotencyReplay); // Marked as replay
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

