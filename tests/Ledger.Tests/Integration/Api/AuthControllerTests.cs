using Ledger.Api.DTOs.Auth;
using Ledger.Api.DTOs.Users;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Repositories;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace Ledger.Tests.Integration.Api;

public class AuthControllerTests : IAsyncLifetime
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

                    // Register repositories and services
                    services.AddScoped<IUserRepository, UserRepository>();
                    services.AddScoped<IPasswordHasher, PasswordHasher>();
                    services.AddScoped<IUserService, UserService>();
                    services.AddScoped<IAuthenticationService, AuthenticationService>();
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
    }

    [Fact]
    public async Task PostLogin_ValidCredentials_Returns200WithToken()
    {
        // Arrange - Create a user first
        var email = "test@example.com";
        var password = "Password123";
        var createRequest = new CreateUserRequest(email, password);
        await _client!.PostAsJsonAsync("/api/users", createRequest);

        var loginRequest = new LoginRequest(email, password);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResponse);
        Assert.NotEmpty(loginResponse.Token);
        Assert.NotEqual(Guid.Empty, loginResponse.UserId);
        Assert.Equal(email, loginResponse.Email);

        // Verify token is valid by using it to access a protected endpoint
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResponse.Token);
        var accountsResponse = await _client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.OK, accountsResponse.StatusCode);
    }

    [Fact]
    public async Task PostLogin_InvalidPassword_Returns401()
    {
        // Arrange - Create a user first
        var email = "test@example.com";
        var password = "Password123";
        var createRequest = new CreateUserRequest(email, password);
        await _client!.PostAsJsonAsync("/api/users", createRequest);

        var loginRequest = new LoginRequest(email, "WrongPassword123");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal("INVALID_CREDENTIALS", problemDetails.Extensions?["reasonCode"]?.ToString());
        Assert.Contains("Invalid email or password", problemDetails.Detail);
    }

    [Fact]
    public async Task PostLogin_InvalidEmail_Returns401()
    {
        // Arrange
        var loginRequest = new LoginRequest("nonexistent@example.com", "Password123");

        // Act
        var response = await _client!.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal("INVALID_CREDENTIALS", problemDetails.Extensions?["reasonCode"]?.ToString());
        // Should have same generic error message (no user enumeration)
        Assert.Contains("Invalid email or password", problemDetails.Detail);
    }

    [Fact]
    public async Task PostLogin_DisabledUser_Returns401()
    {
        // Arrange - Create a user and disable it
        var email = "disabled@example.com";
        var password = "Password123";
        var createRequest = new CreateUserRequest(email, password);
        var createResponse = await _client!.PostAsJsonAsync("/api/users", createRequest);
        var user = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);

        // Disable the user (directly in DB for testing)
        var dbUser = await _context!.Users.FindAsync(user.Id);
        Assert.NotNull(dbUser);
        var disabledUser = dbUser.WithIsEnabled(false);
        _context.Entry(dbUser).CurrentValues.SetValues(new
        {
            disabledUser.Id,
            disabledUser.Email,
            disabledUser.PasswordHash,
            disabledUser.IsEnabled,
            disabledUser.FailedLoginAttempts,
            disabledUser.LastLoginAt,
            disabledUser.CreatedAt,
            disabledUser.UpdatedAt
        });
        _context.Entry(dbUser).State = EntityState.Modified;
        await _context.SaveChangesAsync();

        var loginRequest = new LoginRequest(email, password);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal("INVALID_CREDENTIALS", problemDetails.Extensions?["reasonCode"]?.ToString());
    }

    [Fact]
    public async Task PostLogin_UpdatesLastLoginAt()
    {
        // Arrange - Create a user
        var email = "test@example.com";
        var password = "Password123";
        var createRequest = new CreateUserRequest(email, password);
        var createResponse = await _client!.PostAsJsonAsync("/api/users", createRequest);
        var user = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);

        // Verify LastLoginAt is null initially
        var dbUserBefore = await _context!.Users.FindAsync(user.Id);
        Assert.NotNull(dbUserBefore);
        Assert.Null(dbUserBefore.LastLoginAt);

        var loginRequest = new LoginRequest(email, password);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        response.EnsureSuccessStatusCode();

        // Verify LastLoginAt is updated
        var dbUserAfter = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(dbUserAfter);
        Assert.NotNull(dbUserAfter.LastLoginAt);
        Assert.True(dbUserAfter.LastLoginAt.Value <= DateTime.UtcNow);
        Assert.True(dbUserAfter.LastLoginAt.Value > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task PostLogin_ResetsFailedLoginAttempts()
    {
        // Arrange - Create a user and simulate failed login
        var email = "test@example.com";
        var password = "Password123";
        var createRequest = new CreateUserRequest(email, password);
        var createResponse = await _client!.PostAsJsonAsync("/api/users", createRequest);
        var user = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);

        // Simulate failed login attempts
        var dbUser = await _context!.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(dbUser);
        var userWithFailures = dbUser.WithFailedLoginAttempts(3);
        _context.Entry(dbUser).CurrentValues.SetValues(new
        {
            userWithFailures.Id,
            userWithFailures.Email,
            userWithFailures.PasswordHash,
            userWithFailures.IsEnabled,
            userWithFailures.FailedLoginAttempts,
            userWithFailures.LastLoginAt,
            userWithFailures.CreatedAt,
            userWithFailures.UpdatedAt
        });
        await _context.SaveChangesAsync();

        var loginRequest = new LoginRequest(email, password);

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        response.EnsureSuccessStatusCode();

        // Verify FailedLoginAttempts is reset to 0
        var dbUserAfter = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(dbUserAfter);
        Assert.Equal(0, dbUserAfter.FailedLoginAttempts);
    }

    [Fact]
    public async Task PostLogin_IncrementsFailedLoginAttempts()
    {
        // Arrange - Create a user
        var email = "test@example.com";
        var password = "Password123";
        var createRequest = new CreateUserRequest(email, password);
        var createResponse = await _client!.PostAsJsonAsync("/api/users", createRequest);
        var user = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);

        var loginRequest = new LoginRequest(email, "WrongPassword123");

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Verify FailedLoginAttempts is incremented
        var dbUser = await _context!.Users.FindAsync(user.Id);
        Assert.NotNull(dbUser);
        Assert.True(dbUser.FailedLoginAttempts > 0);
    }

    [Fact]
    public async Task PostLogin_NoUserEnumeration_SameErrorForInvalidEmailAndPassword()
    {
        // Arrange - Create a user
        var email = "test@example.com";
        var password = "Password123";
        var createRequest = new CreateUserRequest(email, password);
        await _client!.PostAsJsonAsync("/api/users", createRequest);

        var invalidEmailRequest = new LoginRequest("nonexistent@example.com", "AnyPassword");
        var invalidPasswordRequest = new LoginRequest(email, "WrongPassword123");

        // Act
        var invalidEmailResponse = await _client.PostAsJsonAsync("/api/auth/login", invalidEmailRequest);
        var invalidPasswordResponse = await _client.PostAsJsonAsync("/api/auth/login", invalidPasswordRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, invalidEmailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidPasswordResponse.StatusCode);

        var invalidEmailProblem = await invalidEmailResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var invalidPasswordProblem = await invalidPasswordResponse.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(invalidEmailProblem);
        Assert.NotNull(invalidPasswordProblem);
        
        // Both should have same error message (no user enumeration)
        Assert.Equal(invalidEmailProblem.Detail, invalidPasswordProblem.Detail);
        Assert.Equal("INVALID_CREDENTIALS", invalidEmailProblem.Extensions?["reasonCode"]?.ToString());
        Assert.Equal("INVALID_CREDENTIALS", invalidPasswordProblem.Extensions?["reasonCode"]?.ToString());
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

