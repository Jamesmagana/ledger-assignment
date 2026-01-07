using Ledger.Api.DTOs.Accounts;
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
using System.Text.Json;
using Testcontainers.PostgreSql;

namespace Ledger.Tests.Integration.Api;

public class AccountsControllerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;

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
                    services.AddScoped<IAccountService, AccountService>();
                });
            });

        _client = _factory.CreateClient();

        // Get DbContext and run migrations
        using var scope = _factory.Services.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await _context.Database.MigrateAsync();
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
    public async Task POST_Accounts_ValidRequest_Returns201Created()
    {
        // Arrange
        var request = new CreateAccountRequest("Cash", AccountType.Asset, true);
        Assert.NotNull(_client);

        // Act
        var response = await _client.PostAsJsonAsync("/api/accounts", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(account);
        Assert.Equal("Cash", account.Name);
        Assert.Equal(AccountType.Asset, account.Type);
        Assert.True(account.IsActive);
        Assert.NotEqual(Guid.Empty, account.Id);
    }

    [Fact]
    public async Task POST_Accounts_DuplicateName_Returns409Conflict()
    {
        // Arrange - create first account
        Assert.NotNull(_client);
        var firstRequest = new CreateAccountRequest("Cash", AccountType.Asset, true);
        await _client.PostAsJsonAsync("/api/accounts", firstRequest);

        // Act - try to create duplicate
        var duplicateRequest = new CreateAccountRequest("Cash", AccountType.Liability, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", duplicateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DUPLICATE_ACCOUNT_NAME", problemDetails.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task POST_Accounts_DuplicateNameCaseInsensitive_Returns409Conflict()
    {
        // Arrange - create first account
        Assert.NotNull(_client);
        var firstRequest = new CreateAccountRequest("Cash", AccountType.Asset, true);
        await _client.PostAsJsonAsync("/api/accounts", firstRequest);

        // Act - try to create duplicate with different case
        var duplicateRequest = new CreateAccountRequest("CASH", AccountType.Liability, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", duplicateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DUPLICATE_ACCOUNT_NAME", problemDetails.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task POST_Accounts_InvalidName_Returns400BadRequest()
    {
        // Arrange
        Assert.NotNull(_client);
        var request = new CreateAccountRequest("", AccountType.Asset, true);

        // Act
        var response = await _client.PostAsJsonAsync("/api/accounts", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GET_Accounts_ReturnsAllAccounts()
    {
        // Arrange - create some accounts
        Assert.NotNull(_client);
        await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Cash", AccountType.Asset, true));
        await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Revenue", AccountType.Revenue, true));

        // Act
        var response = await _client.GetAsync("/api/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>();
        Assert.NotNull(accounts);
        Assert.True(accounts.Count >= 2);
    }

    [Fact]
    public async Task GET_Accounts_ById_ExistingAccount_Returns200OK()
    {
        // Arrange - create account
        Assert.NotNull(_client);
        var createResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Cash", AccountType.Asset, true));
        var createdAccount = await createResponse.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(createdAccount);

        // Act
        var response = await _client.GetAsync($"/api/accounts/{createdAccount.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(account);
        Assert.Equal(createdAccount.Id, account.Id);
        Assert.Equal("Cash", account.Name);
    }

    [Fact]
    public async Task GET_Accounts_ById_NotFound_Returns404NotFound()
    {
        // Arrange
        Assert.NotNull(_client);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/accounts/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PUT_Accounts_ById_UpdateIsActive_Returns200OK()
    {
        // Arrange - create account
        Assert.NotNull(_client);
        var createResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Cash", AccountType.Asset, true));
        var createdAccount = await createResponse.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(createdAccount);

        // Act - update IsActive
        var updateRequest = new UpdateAccountRequest(false);
        var response = await _client.PutAsJsonAsync($"/api/accounts/{createdAccount.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updatedAccount = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(updatedAccount);
        Assert.False(updatedAccount.IsActive);
        Assert.Equal(createdAccount.Id, updatedAccount.Id);
    }

    [Fact]
    public async Task PUT_Accounts_ById_NotFound_Returns404NotFound()
    {
        // Arrange
        Assert.NotNull(_client);
        var nonExistentId = Guid.NewGuid();
        var updateRequest = new UpdateAccountRequest(false);

        // Act
        var response = await _client.PutAsJsonAsync($"/api/accounts/{nonExistentId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_Accounts_IncludesCorrelationId()
    {
        // Arrange
        Assert.NotNull(_client);
        var correlationId = Guid.NewGuid().ToString();
        var request = new CreateAccountRequest("Cash", AccountType.Asset, true);
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Act
        var response = await _client.PostAsJsonAsync("/api/accounts", request);

        // Assert
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var responseCorrelationId = response.Headers.GetValues("X-Correlation-Id").First();
        Assert.Equal(correlationId, responseCorrelationId);
    }
}

