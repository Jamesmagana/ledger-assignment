using Ledger.Api.DTOs.Accounts;
using Ledger.Api.DTOs.JournalEntries;
using Ledger.Api.DTOs.Reports;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Repositories;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace Ledger.Tests.Integration.Api;

public class TrialBalanceControllerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private Guid? _cashAccountId;
    private Guid? _revenueAccountId;
    private Guid? _expenseAccountId;
    private Guid? _zeroActivityAccountId;

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

        // Create WebApplicationFactory with test database
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

                    // Register repositories and services
                    services.AddScoped<IAccountRepository, AccountRepository>();
                    services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
                    services.AddScoped<ITrialBalanceRepository, TrialBalanceRepository>();
                    services.AddScoped<IAccountService, AccountService>();
                    services.AddScoped<IRequestHashService, RequestHashService>();
                    services.AddScoped<IJournalEntryService, JournalEntryService>();
                    services.AddScoped<ITrialBalanceService, TrialBalanceService>();
                });
            });

        _client = _factory.CreateClient();

        // Get DbContext and run migrations
        using var scope = _factory.Services.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await _context.Database.MigrateAsync();

        // Create test accounts
        var cashResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Cash", AccountType.Asset, true));
        var cashAccount = await cashResponse.Content.ReadFromJsonAsync<AccountResponse>();
        _cashAccountId = cashAccount?.Id;

        var revenueResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Revenue", AccountType.Revenue, true));
        var revenueAccount = await revenueResponse.Content.ReadFromJsonAsync<AccountResponse>();
        _revenueAccountId = revenueAccount?.Id;

        var expenseResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Expense", AccountType.Expense, true));
        var expenseAccount = await expenseResponse.Content.ReadFromJsonAsync<AccountResponse>();
        _expenseAccountId = expenseAccount?.Id;

        var zeroActivityResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("ZeroActivity", AccountType.Asset, true));
        var zeroActivityAccount = await zeroActivityResponse.Content.ReadFromJsonAsync<AccountResponse>();
        _zeroActivityAccountId = zeroActivityAccount?.Id;
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }

        _client?.Dispose();
        _factory?.Dispose();

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
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
        var trialBalance = await response.Content.ReadFromJsonAsync<TrialBalanceResponse>();
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

        await _client.PostAsJsonAsync("/api/journal-entries", entryRequest);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await response.Content.ReadFromJsonAsync<TrialBalanceResponse>();
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

        await _client.PostAsJsonAsync("/api/journal-entries", entry1);
        await _client.PostAsJsonAsync("/api/journal-entries", entry2);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await response.Content.ReadFromJsonAsync<TrialBalanceResponse>();
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

        await _client.PostAsJsonAsync("/api/journal-entries", entry1);
        await _client.PostAsJsonAsync("/api/journal-entries", entry2);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await response.Content.ReadFromJsonAsync<TrialBalanceResponse>();
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

        await _client.PostAsJsonAsync("/api/journal-entries", entry1);

        // Wait a moment to ensure different timestamps
        await Task.Delay(100);

        // Get the asOf date (now, before second entry)
        var asOf = DateTime.UtcNow;

        // Create second entry (after asOf)
        var entry2 = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 50.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 50.00m)
            });

        await _client.PostAsJsonAsync("/api/journal-entries", entry2);

        // Act - Get trial balance as of the date between entries
        var response = await _client.GetAsync($"/api/reports/trial-balance?asOf={asOf:yyyy-MM-ddTHH:mm:ssZ}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await response.Content.ReadFromJsonAsync<TrialBalanceResponse>();
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

        await _client.PostAsJsonAsync("/api/journal-entries", entryRequest);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await response.Content.ReadFromJsonAsync<TrialBalanceResponse>();
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
        var trialBalance = await response.Content.ReadFromJsonAsync<TrialBalanceResponse>();
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

        await _client.PostAsJsonAsync("/api/journal-entries", entryRequest);

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var trialBalance = await response.Content.ReadFromJsonAsync<TrialBalanceResponse>();
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

