---
name: User Management and Login Implementation
overview: Implement database-backed user management with secure password hashing and JWT token issuance. Create User entity, password hashing service, user repository/service, authentication controller, and comprehensive tests.
todos: []
---

# User Management and Login Implementation Plan

## Overview

Implement database-backed user management with secure authentication. Users are stored in the database with hashed passwords. Login endpoint authenticates users and issues JWT tokens. All security best practices are enforced (timing-safe verification, no user enumeration, password strength validation).

## Current State

- JWT authentication is configured and working
- Domain entities follow `BaseEntity` pattern
- Repository pattern established (I*Repository interfaces in Application, implementations in Infrastructure)
- Service pattern established (I*Service interfaces and implementations in Application)
- EF Core configurations in `Infrastructure/Data/Configurations/`
- Case-insensitive unique constraints implemented for Account.Name
- No User entity or authentication logic yet

## Implementation Tasks

### 1. Domain Entity: User

**File:** `src/Ledger.Domain/Entities/User.cs`Create User entity following BaseEntity pattern:

```csharp
public class User : BaseEntity
{
    public string Email { get; init; } // Unique, case-insensitive
    public string PasswordHash { get; init; } // Never exposed
    public bool IsEnabled { get; init; } // Soft state (enable/disable)
    public int FailedLoginAttempts { get; init; } // Track failed logins
    public DateTime? LastLoginAt { get; init; } // Last successful login
}
```

**Design Decisions:**

- Inherits from `BaseEntity` (Id, CreatedAt, UpdatedAt)
- Email is unique case-insensitively (enforced at DB level)
- PasswordHash stored, never returned in API responses
- IsEnabled for soft state (default: true)
- FailedLoginAttempts and LastLoginAt for security tracking
- Immutability pattern: use `With*` methods for updates (similar to Account.WithIsActive)

### 2. EF Core Configuration

**File:** `src/Ledger.Infrastructure/Data/Configurations/UserConfiguration.cs`Configure User entity:

- Email: required, max length 200, case-insensitive unique index (UPPER)
- PasswordHash: required, max length 200 (bcrypt hashes are ~60 chars)
- IsEnabled: required, default true
- FailedLoginAttempts: required, default 0
- LastLoginAt: nullable timestamp
- Timestamps: UTC with defaults

**Unique Index:**

- Similar to Account.Name: `CREATE UNIQUE INDEX "IX_Users_Email_Normalized" ON "Users" (UPPER("Email"));`

### 3. Database Migration

**Command:** `dotnet ef migrations add AddUserEntity`**File:** `src/Ledger.Infrastructure/Migrations/YYYYMMDDHHMMSS_AddUserEntity.cs`Migration will:

- Create Users table
- Add case-insensitive unique index on Email
- Set up foreign key constraints if needed (none for User)
- Add CHECK constraints if needed

### 4. Password Hashing Service

**File:** `src/Ledger.Application/Services/IPasswordHasher.cs`**File:** `src/Ledger.Application/Services/PasswordHasher.cs`Create password hashing service interface and implementation:

- Use `BCrypt.Net-Next` package (recommended for .NET) or `Microsoft.AspNetCore.Identity.PasswordHasher<T>`
- Methods:
- `HashPassword(string password) -> string` - Hash password with salt
- `VerifyPassword(string password, string hash) -> bool` - Timing-safe verification

**Design Decisions:**

- Use BCrypt for password hashing (industry standard, adaptive)
- Timing-safe comparison (BCrypt handles this internally)
- No plain text passwords stored or returned
- Password strength validation in service layer (min length, complexity)

**Package:** `BCrypt.Net-Next` (version 0.1.0 or latest)

### 5. JWT Token Generation Service

**File:** `src/Ledger.Application/Services/IJwtTokenService.cs`**File:** `src/Ledger.Application/Services/JwtTokenService.cs`Create service to generate JWT tokens:

- Inject `JwtSettings` (via IOptions or direct)
- Method: `GenerateToken(Guid userId, string email) -> string`
- Token claims:
- `sub` (subject): userId
- `email`: user email
- `iat` (issued at): current time
- `exp` (expiration): configured expiration
- `nbf` (not before): current time

**Design Decisions:**

- Use `System.IdentityModel.Tokens.Jwt` (already available via JWT Bearer package)
- Token expiration: configurable (default 1 hour)
- Sign with same secret key as JWT validation

### 6. User Repository

**File:** `src/Ledger.Application/Repositories/IUserRepository.cs`**File:** `src/Ledger.Infrastructure/Repositories/UserRepository.cs`Repository interface and implementation:

- `GetByIdAsync(Guid id) -> User?`
- `FindByEmailAsync(string email) -> User?` (case-insensitive)
- `AddAsync(User user) -> User`
- `UpdateAsync(User user) -> User` (for failed login attempts, last login)

**Design Decisions:**

- Follow existing repository pattern
- Case-insensitive email lookup using EF Core ToUpper
- AsNoTracking for read operations
- Track changes for updates

### 7. User Service

**File:** `src/Ledger.Application/Services/IUserService.cs`**File:** `src/Ledger.Application/Services/UserService.cs`User business logic:

- `CreateUserAsync(string email, string password) -> User`
- Validate email format
- Validate password strength (min 8 chars, complexity)
- Check for duplicate email (case-insensitive)
- Hash password
- Create user entity (IsEnabled = true, FailedLoginAttempts = 0)
- Save to repository
- `AuthenticateAsync(string email, string password) -> User?`
- Find user by email (case-insensitive)
- Verify password (timing-safe)
- Check if user is enabled
- Update FailedLoginAttempts and LastLoginAt
- Return user if authenticated, null otherwise

**Design Decisions:**

- Password strength: minimum 8 characters, at least one letter and one number
- Generic error on authentication failure (no user enumeration)
- Always perform password verification (timing-safe) even if user not found
- Update FailedLoginAttempts on failure, reset on success
- Update LastLoginAt on successful login

### 8. Authentication Service

**File:** `src/Ledger.Application/Services/IAuthenticationService.cs`**File:** `src/Ledger.Application/Services/AuthenticationService.cs`Orchestrates login flow:

- `LoginAsync(string email, string password) -> LoginResult`
- Call UserService.AuthenticateAsync
- If authenticated, generate JWT token
- Return LoginResult with token and user info (no password hash)
- If not authenticated, throw UnauthorizedException with generic message

**Design Decisions:**

- Separate service for authentication orchestration
- LoginResult contains: Token (string), UserId (Guid), Email (string)
- Generic error message: "Invalid email or password" (no user enumeration)

### 9. DTOs

**File:** `src/Ledger.Api/DTOs/Users/CreateUserRequest.cs`

```csharp
public record CreateUserRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password
);
```

**File:** `src/Ledger.Api/DTOs/Users/UserResponse.cs`

```csharp
public class UserResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    // NO PasswordHash, NO FailedLoginAttempts (security)
}
```

**File:** `src/Ledger.Api/DTOs/Auth/LoginRequest.cs`

```csharp
public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);
```

**File:** `src/Ledger.Api/DTOs/Auth/LoginResponse.cs`

```csharp
public class LoginResponse
{
    public string Token { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; }
}
```



### 10. Controllers

**File:** `src/Ledger.Api/Controllers/UsersController.cs`

```csharp
[Authorize] // Only authenticated users can create users (or make public if needed)
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(UserResponse), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        // Call UserService.CreateUserAsync
        // Map to UserResponse (exclude PasswordHash)
        // Return 201 Created
    }
}
```

**File:** `src/Ledger.Api/Controllers/AuthController.cs`

```csharp
[ApiController]
[Route("api/auth")]
[AllowAnonymous] // Login endpoint must be public
public class AuthController : ControllerBase
{
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Call AuthenticationService.LoginAsync
        // Return 200 OK with LoginResponse (contains JWT token)
        // On failure, return 401 with generic error message
    }
}
```

**Design Decisions:**

- UsersController may or may not require authentication (decide: should unauthenticated users be able to register?)
- AuthController.Login must be `[AllowAnonymous]` (public endpoint)
- All error responses use ProblemDetails with reasonCode and correlationId

### 11. DbContext Updates

**File:** `src/Ledger.Infrastructure/Data/LedgerDbContext.cs`Add:

```csharp
public DbSet<User> Users { get; set; } = null!;
```



### 12. Dependency Injection

**File:** `src/Ledger.Api/Program.cs`Register services:

```csharp
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
```



### 13. Unit Tests

**File:** `tests/Ledger.Tests/Application/Services/PasswordHasherTests.cs`

- Hash password returns non-empty hash
- Verify correct password returns true
- Verify incorrect password returns false
- Timing-safe verification (measure time differences)

**File:** `tests/Ledger.Tests/Application/Services/JwtTokenServiceTests.cs`

- Generate token includes correct claims (sub, email, exp, nbf)
- Token is valid and can be validated
- Token expiration is correct

**File:** `tests/Ledger.Tests/Application/Services/UserServiceTests.cs`

- CreateUserAsync validates email format
- CreateUserAsync validates password strength
- CreateUserAsync rejects duplicate email
- CreateUserAsync hashes password
- AuthenticateAsync verifies correct password
- AuthenticateAsync rejects incorrect password
- AuthenticateAsync rejects disabled user
- AuthenticateAsync updates FailedLoginAttempts
- AuthenticateAsync updates LastLoginAt on success
- Generic error on authentication failure (no user enumeration)

**File:** `tests/Ledger.Tests/Application/Services/AuthenticationServiceTests.cs`

- LoginAsync returns token on success
- LoginAsync throws UnauthorizedException on failure
- LoginResult contains correct user info (no password hash)

### 14. Integration Tests

**File:** `tests/Ledger.Tests/Integration/Api/UsersControllerTests.cs`

- POST /api/users with valid data returns 201
- POST /api/users with duplicate email returns 409
- POST /api/users with invalid email returns 400
- POST /api/users with weak password returns 400
- UserResponse does not contain PasswordHash
- Email uniqueness is case-insensitive

**File:** `tests/Ledger.Tests/Integration/Api/AuthControllerTests.cs`

- POST /api/auth/login with valid credentials returns 200 with token
- POST /api/auth/login with invalid email returns 401 (generic error)
- POST /api/auth/login with invalid password returns 401 (generic error)
- POST /api/auth/login with disabled user returns 401
- Returned token is valid and can be used for authenticated requests
- FailedLoginAttempts is incremented on failure
- LastLoginAt is updated on success
- No user enumeration (same error for invalid email vs invalid password)

### 15. Documentation Updates

**File:** `PROMPTS.md`

- Add Phase 7 entry documenting:
- User entity design
- Password hashing approach (BCrypt)
- Authentication flow
- Security considerations (timing-safe, no enumeration)
- JWT token generation integration

**File:** `docs/VALIDATION_MATRIX.md`

- Update USER CREATION section with implemented status
- Update USER LOGIN section with implemented status
- Mark all validations as tested

## File Structure

```javascript
src/Ledger.Domain/
├── Entities/
│   └── User.cs (new)

src/Ledger.Application/
├── Repositories/
│   └── IUserRepository.cs (new)
├── Services/
│   ├── IPasswordHasher.cs (new)
│   ├── PasswordHasher.cs (new)
│   ├── IJwtTokenService.cs (new)
│   ├── JwtTokenService.cs (new)
│   ├── IUserService.cs (new)
│   ├── UserService.cs (new)
│   ├── IAuthenticationService.cs (new)
│   └── AuthenticationService.cs (new)

src/Ledger.Infrastructure/
├── Data/
│   ├── Configurations/
│   │   └── UserConfiguration.cs (new)
│   └── LedgerDbContext.cs (update)
├── Repositories/
│   └── UserRepository.cs (new)
└── Migrations/
    └── YYYYMMDDHHMMSS_AddUserEntity.cs (new)

src/Ledger.Api/
├── Controllers/
│   ├── UsersController.cs (new)
│   └── AuthController.cs (new)
├── DTOs/
│   ├── Users/
│   │   ├── CreateUserRequest.cs (new)
│   │   └── UserResponse.cs (new)
│   └── Auth/
│       ├── LoginRequest.cs (new)
│       └── LoginResponse.cs (new)
└── Program.cs (update - register services)

tests/Ledger.Tests/
├── Application/
│   └── Services/
│       ├── PasswordHasherTests.cs (new)
│       ├── JwtTokenServiceTests.cs (new)
│       ├── UserServiceTests.cs (new)
│       └── AuthenticationServiceTests.cs (new)
└── Integration/
    └── Api/
        ├── UsersControllerTests.cs (new)
        └── AuthControllerTests.cs (new)
```



## Security Considerations

1. **Password Hashing:**

- Use BCrypt (adaptive, salt included in hash)
- Never store plain text passwords
- Never return password hash in API responses

2. **Timing-Safe Verification:**

- BCrypt verification is timing-safe
- Always perform verification even if user not found (prevents user enumeration)

3. **No User Enumeration:**

- Generic error message: "Invalid email or password"
- Same error for invalid email vs invalid password
- Same response time regardless of failure reason

4. **Password Strength:**

- Minimum 8 characters
- At least one letter and one number
- Reject common weak passwords

5. **Failed Login Tracking:**

- Increment FailedLoginAttempts on failure
- Reset on success
- Could add account lockout after N attempts (future enhancement)

6. **JWT Token Security:**

- Tokens signed with secret key
- Tokens include expiration
- Tokens include user ID and email (no sensitive data)

## Implementation Details

### Password Hashing (BCrypt)

```csharp
public class PasswordHasher : IPasswordHasher
{
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    public bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
```



### JWT Token Generation

```csharp
public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly SymmetricSecurityKey _signingKey;

    public string GenerateToken(Guid userId, string email)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            notBefore: DateTime.UtcNow,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```



### Authentication Flow

1. User submits email and password
2. UserService.AuthenticateAsync:

- Find user by email (case-insensitive)
- Verify password (timing-safe, even if user not found)
- Check if user is enabled
- Update FailedLoginAttempts (increment on failure, reset on success)
- Update LastLoginAt on success
- Return user if authenticated, null otherwise

3. AuthenticationService.LoginAsync:

- Call UserService.AuthenticateAsync
- If authenticated, generate JWT token
- Return LoginResult with token
- If not authenticated, throw UnauthorizedException

## Verification Steps

1. User entity created with proper fields
2. Database migration creates Users table with unique email index
3. Password hashing works correctly
4. JWT token generation works and tokens are valid
5. User creation validates email and password
6. User creation rejects duplicate emails (case-insensitive)
7. Login authenticates correctly
8. Login rejects invalid credentials with generic error
9. Login rejects disabled users
10. FailedLoginAttempts and LastLoginAt are tracked
11. No password hash in API responses
12. All tests pass
13. Documentation updated

## Critical Considerations

- **No User Enumeration:** Always return generic error message
- **Timing-Safe Verification:** Always perform password verification