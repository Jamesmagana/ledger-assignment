using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ledger.Api.DTOs.Accounts;
using Ledger.Api.DTOs.Auth;
using Ledger.Api.DTOs.JournalEntries;
using Ledger.Api.DTOs.Reports;
using Ledger.Api.DTOs.Users;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Repositories;
using Ledger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Tests.Integration.Api;

public class TrialBalanceControllerTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private IServiceScope? _serviceScope;
    private readonly string _databaseName = $"TrialBalanceTest_{Guid.NewGuid()}";
    private Guid? _cashAccountId;
    private Guid? _revenueAccountId;
    private Guid? _expenseAccountId;
    private Guid? _zeroActivityAccountId;

    public async Task InitializeAsync()
    {
        // Create WebApplicationFactory with in-memory test database
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
                            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
                    });

                    // Register repositories and services
                    services.AddScoped<IAccountRepository, AccountRepository>();
                    services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
                    services.AddScoped<ITrialBalanceRepository, TrialBalanceRepository>();
                    services.AddScoped<IAccountService, AccountService>();
                    services.AddScoped<IRequestHashService, RequestHashService>();
                    services.AddScoped<IJournalEntryService, JournalEntryService>();
                    services.AddScoped<ITrialBalanceService, TrialBalanceService>();
                });

                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "Jwt:Issuer", "https://ledger-api.test" },
                        { "Jwt:Audience", "https://ledger-api.test" },
                        { "Jwt:SecretKey", "TestSecretKey_ForIntegrationTests_Minimum32Characters" },
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
        var email = "trialbalance@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create test accounts
        var cashResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Cash", AccountType.Asset, true));
        cashResponse.EnsureSuccessStatusCode();
        var cashAccount = await cashResponse.Content.ReadFromJsonAsync<AccountResponse>(JsonHelper.GetJsonOptions());
        _cashAccountId = cashAccount?.Id;

        var revenueResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Revenue", AccountType.Revenue, true));
        revenueResponse.EnsureSuccessStatusCode();
        var revenueAccount = await revenueResponse.Content.ReadFromJsonAsync<AccountResponse>(JsonHelper.GetJsonOptions());
        _revenueAccountId = revenueAccount?.Id;

        var expenseResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Expense", AccountType.Expense, true));
        expenseResponse.EnsureSuccessStatusCode();
        var expenseAccount = await expenseResponse.Content.ReadFromJsonAsync<AccountResponse>(JsonHelper.GetJsonOptions());
        _expenseAccountId = expenseAccount?.Id;

        var zeroActivityResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("ZeroActivity", AccountType.Asset, true));
        zeroActivityResponse.EnsureSuccessStatusCode();
        var zeroActivityAccount = await zeroActivityResponse.Content.ReadFromJsonAsync<AccountResponse>(JsonHelper.GetJsonOptions());
        _zeroActivityAccountId = zeroActivityAccount?.Id;
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

    [Fact]
    public async Task GET_TrialBalance_NoJournalEntries_ReturnsAllAccountsWithZeroNet()
    {
        // Arrange
        Assert.NotNull(_client);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await JsonHelper.ReadFromJsonAsync<TrialBalanceResponse>(response.Content);
        Assert.NotNull(trialBalance);
        Assert.True(trialBalance.Items.Count >= 4); // At least our test accounts
        Assert.All(trialBalance.Items, item =>
        {
            Assert.Equal(0, item.TotalDebits);
            Assert.Equal(0, item.TotalCredits);
            Assert.Equal(0, item.Net);
        });
        Assert.Equal(0, trialBalance.TotalNet);
    }

    [Fact]
    public async Task GET_TrialBalance_WithJournalEntries_ReturnsCorrectBalances()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);

        // Create a journal entry: Cash (Debit) 100, Revenue (Credit) 100
        var entryRequest = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        await _client.PostAsJsonAsync("/api/JournalEntries", entryRequest);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await JsonHelper.ReadFromJsonAsync<TrialBalanceResponse>(response.Content);
        Assert.NotNull(trialBalance);
        Assert.Equal(0, trialBalance.TotalNet);

        var cashItem = trialBalance.Items.FirstOrDefault(i => i.AccountId == _cashAccountId.Value);
        Assert.NotNull(cashItem);
        Assert.Equal(100.00m, cashItem.TotalDebits);
        Assert.Equal(0, cashItem.TotalCredits);
        Assert.Equal(100.00m, cashItem.Net);

        var revenueItem = trialBalance.Items.FirstOrDefault(i => i.AccountId == _revenueAccountId.Value);
        Assert.NotNull(revenueItem);
        Assert.Equal(0, revenueItem.TotalDebits);
        Assert.Equal(100.00m, revenueItem.TotalCredits);
        Assert.Equal(-100.00m, revenueItem.Net); // Credits are negative for net

        // Zero-activity account should still be included
        Assert.NotNull(_zeroActivityAccountId);
        var zeroActivityItem = trialBalance.Items.FirstOrDefault(i => i.AccountId == _zeroActivityAccountId.Value);
        Assert.NotNull(zeroActivityItem);
        Assert.Equal(0, zeroActivityItem.TotalDebits);
        Assert.Equal(0, zeroActivityItem.TotalCredits);
        Assert.Equal(0, zeroActivityItem.Net);
    }

    [Fact]
    public async Task GET_TrialBalance_EachAccountAppearsOnce()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);
        Assert.NotNull(_expenseAccountId);

        // Create multiple journal entries
        var entry1 = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        var entry2 = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_expenseAccountId.Value, LineDirection.Debit, 50.00m),
                new(_cashAccountId.Value, LineDirection.Credit, 50.00m)
            });

        await _client.PostAsJsonAsync("/api/JournalEntries", entry1);
        await _client.PostAsJsonAsync("/api/JournalEntries", entry2);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await JsonHelper.ReadFromJsonAsync<TrialBalanceResponse>(response.Content);
        Assert.NotNull(trialBalance);

        // Verify each account appears exactly once
        var accountIds = trialBalance.Items.Select(i => i.AccountId).ToList();
        var uniqueAccountIds = accountIds.Distinct().ToList();
        Assert.Equal(accountIds.Count, uniqueAccountIds.Count);

        // Verify Cash account appears once with correct aggregation
        var cashItems = trialBalance.Items.Where(i => i.AccountId == _cashAccountId.Value).ToList();
        Assert.Single(cashItems);
        var cashItem = cashItems[0];
        Assert.Equal(100.00m, cashItem.TotalDebits); // From entry1
        Assert.Equal(50.00m, cashItem.TotalCredits); // From entry2
        Assert.Equal(50.00m, cashItem.Net); // 100 - 50
    }

    [Fact]
    public async Task GET_TrialBalance_TotalNetEqualsZero()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);
        Assert.NotNull(_expenseAccountId);

        // Create multiple balanced journal entries
        var entry1 = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        var entry2 = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_expenseAccountId.Value, LineDirection.Debit, 50.00m),
                new(_cashAccountId.Value, LineDirection.Credit, 50.00m)
            });

        await _client.PostAsJsonAsync("/api/JournalEntries", entry1);
        await _client.PostAsJsonAsync("/api/JournalEntries", entry2);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await JsonHelper.ReadFromJsonAsync<TrialBalanceResponse>(response.Content);
        Assert.NotNull(trialBalance);
        Assert.Equal(0, trialBalance.TotalNet);

        // Verify manual calculation
        var calculatedTotal = trialBalance.Items.Sum(item => item.Net);
        Assert.Equal(0, calculatedTotal);
    }

    [Fact]
    public async Task GET_TrialBalance_WithAsOfFilter_FiltersCorrectly()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);

        var now = DateTime.UtcNow;
        var futureDate = now.AddDays(1);

        // Create first entry (before asOf)
        var entry1 = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        var response1 = await _client.PostAsJsonAsync("/api/JournalEntries", entry1);
        response1.EnsureSuccessStatusCode();
        var createdEntry1 = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(response1.Content);
        Assert.NotNull(createdEntry1);

        // Wait a moment to ensure different timestamps and ensure the first entry is fully persisted
        await Task.Delay(200);

        // Get the asOf date - use the first entry's PostedAt plus a small offset to ensure it's included
        // This ensures the asOf date is definitely after the first entry but before the second
        var asOf = createdEntry1.PostedAt.AddMilliseconds(100);

        // Wait a bit more to ensure asOf is definitely before the second entry's PostedAt
        await Task.Delay(100);

        // Create second entry (after asOf)
        var entry2 = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 50.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 50.00m)
            });

        var response2 = await _client.PostAsJsonAsync("/api/JournalEntries", entry2);
        response2.EnsureSuccessStatusCode();

        // Act - Get trial balance as of the date between entries
        // Use ISO 8601 format (round-trip format) for reliable parsing
        var asOfString = Uri.EscapeDataString(asOf.ToString("o"));
        var response = await _client.GetAsync($"/api/reports/trial-balance?asOf={asOfString}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await JsonHelper.ReadFromJsonAsync<TrialBalanceResponse>(response.Content);
        Assert.NotNull(trialBalance);
        Assert.Equal(0, trialBalance.TotalNet);

        var cashItem = trialBalance.Items.FirstOrDefault(i => i.AccountId == _cashAccountId.Value);
        Assert.NotNull(cashItem);
        // Should only include first entry (100 debit, 0 credit)
        Assert.Equal(100.00m, cashItem.TotalDebits);
        Assert.Equal(0, cashItem.TotalCredits);
        Assert.Equal(100.00m, cashItem.Net);
    }

    [Fact]
    public async Task GET_TrialBalance_ZeroActivityAccountsIncluded()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);
        Assert.NotNull(_zeroActivityAccountId);

        // Create a journal entry (doesn't use zero-activity account)
        var entryRequest = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        await _client.PostAsJsonAsync("/api/JournalEntries", entryRequest);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await JsonHelper.ReadFromJsonAsync<TrialBalanceResponse>(response.Content);
        Assert.NotNull(trialBalance);

        // Verify zero-activity account is included
        var zeroActivityItem = trialBalance.Items.FirstOrDefault(i => i.AccountId == _zeroActivityAccountId.Value);
        Assert.NotNull(zeroActivityItem);
        Assert.Equal("ZeroActivity", zeroActivityItem.AccountName);
        Assert.Equal(0, zeroActivityItem.TotalDebits);
        Assert.Equal(0, zeroActivityItem.TotalCredits);
        Assert.Equal(0, zeroActivityItem.Net);
    }

    [Fact]
    public async Task GET_TrialBalance_AccountsOrderedByName()
    {
        // Arrange
        Assert.NotNull(_client);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await JsonHelper.ReadFromJsonAsync<TrialBalanceResponse>(response.Content);
        Assert.NotNull(trialBalance);

        // Verify accounts are ordered by name
        var accountNames = trialBalance.Items.Select(i => i.AccountName).ToList();
        var sortedNames = accountNames.OrderBy(n => n).ToList();
        Assert.Equal(sortedNames, accountNames);
    }

    [Fact]
    public async Task GET_TrialBalance_NetCalculationCorrect()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);
        Assert.NotNull(_expenseAccountId);

        // Create entry: Cash (Debit) 200, Revenue (Credit) 150, Expense (Credit) 50
        var entryRequest = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 200.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 150.00m),
                new(_expenseAccountId.Value, LineDirection.Credit, 50.00m)
            });

        await _client.PostAsJsonAsync("/api/JournalEntries", entryRequest);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await JsonHelper.ReadFromJsonAsync<TrialBalanceResponse>(response.Content);
        Assert.NotNull(trialBalance);

        var cashItem = trialBalance.Items.FirstOrDefault(i => i.AccountId == _cashAccountId.Value);
        Assert.NotNull(cashItem);
        Assert.Equal(200.00m, cashItem.TotalDebits);
        Assert.Equal(0, cashItem.TotalCredits);
        Assert.Equal(200.00m, cashItem.Net); // Debits - Credits

        var revenueItem = trialBalance.Items.FirstOrDefault(i => i.AccountId == _revenueAccountId.Value);
        Assert.NotNull(revenueItem);
        Assert.Equal(0, revenueItem.TotalDebits);
        Assert.Equal(150.00m, revenueItem.TotalCredits);
        Assert.Equal(-150.00m, revenueItem.Net); // 0 - 150

        var expenseItem = trialBalance.Items.FirstOrDefault(i => i.AccountId == _expenseAccountId.Value);
        Assert.NotNull(expenseItem);
        Assert.Equal(0, expenseItem.TotalDebits);
        Assert.Equal(50.00m, expenseItem.TotalCredits);
        Assert.Equal(-50.00m, expenseItem.Net); // 0 - 50

        // Total net should be 0
        Assert.Equal(0, trialBalance.TotalNet);
    }
}

