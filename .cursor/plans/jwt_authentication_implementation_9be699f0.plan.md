---
name: JWT Authentication Implementation
overview: Implement simple JWT Bearer authentication for all API endpoints. Valid JWT grants access; missing/invalid/expired JWT returns 401 with RFC7807 ProblemDetails. Configuration-driven with no hardcoded secrets.
todos:
  - id: add-jwt-package
    content: Add Microsoft.AspNetCore.Authentication.JwtBearer package to API project
    status: in_progress
  - id: create-jwt-configuration
    content: Create JwtSettings configuration class and add JWT settings to appsettings.json files
    status: pending
  - id: configure-jwt-authentication
    content: Configure JWT authentication in Program.cs with TokenValidationParameters (signature, issuer, audience, exp, nbf, clock skew)
    status: pending
    dependencies:
      - add-jwt-package
      - create-jwt-configuration
  - id: configure-authentication-challenge
    content: Configure JWT Bearer challenge event to return RFC7807 ProblemDetails with reasonCode and correlationId
    status: pending
    dependencies:
      - configure-jwt-authentication
  - id: add-authorize-attributes
    content: Add [Authorize] attribute to all controllers (AccountsController, JournalEntriesController, ReportsController)
    status: pending
    dependencies:
      - configure-jwt-authentication
  - id: configure-swagger-jwt
    content: Configure Swagger/OpenAPI to support JWT Bearer token authentication
    status: pending
    dependencies:
      - configure-jwt-authentication
  - id: update-exception-handling
    content: Update ExceptionHandlingMiddleware to handle authentication exceptions with appropriate reasonCodes
    status: pending
    dependencies:
      - configure-jwt-authentication
  - id: create-unit-tests
    content: Create unit tests for JWT authentication covering missing/invalid/expired tokens and ProblemDetails responses
    status: pending
    dependencies:
      - configure-authentication-challenge
  - id: create-integration-tests
    content: Create integration tests for authentication using Testcontainers, covering all endpoints and token scenarios
    status: pending
    dependencies:
      - add-authorize-attributes
  - id: update-validation-matrix
    content: Update VALIDATION_MATRIX.md to reflect implemented and tested JWT authentication validations
    status: pending
    dependencies:
      - create-integration-tests
  - id: update-prompts
    content: Update PROMPTS.md with Phase 6 JWT authentication implementation decisions
    status: pending
    dependencies:
      - create-integration-tests
---

# JWT Authentication Implementation Plan

## Overview

Implement simple JWT Bearer authentication that protects all API endpoints. Authentication only (no roles/permissions). Valid JWT grants access; missing/invalid/expired JWT returns 401 Unauthorized with RFC7807 ProblemDetails including correlationId.

## Current State

- `UseAuthorization()` middleware is registered but no authentication is configured
- No controllers have `[Authorize]` attributes
- `UnauthorizedException` exists in Application layer
- Exception handling middleware in place
- No JWT packages or configuration yet
- Health check endpoint exists (may need to be excluded from auth)

## Implementation Tasks

### 1. Configuration

**File:** `src/Ledger.Api/appsettings.json`Add JWT configuration section:

```json
{
  "Jwt": {
    "Issuer": "https://ledger-api.example.com",
    "Audience": "https://ledger-api.example.com",
    "SecretKey": "CHANGE_THIS_IN_PRODUCTION_USE_ENVIRONMENT_VARIABLE",
    "ClockSkewSeconds": 300
  }
}
```

**File:** `src/Ledger.Api/appsettings.Development.json`Override with development values:

```json
{
  "Jwt": {
    "Issuer": "https://ledger-api.local",
    "Audience": "https://ledger-api.local",
    "SecretKey": "DevelopmentSecretKey_NotForProduction_UseStrongKey",
    "ClockSkewSeconds": 60
  }
}
```

**Design Decisions:**

- SecretKey should be read from environment variable in production
- Issuer and Audience can be environment-specific
- ClockSkewSeconds defaults to 5 minutes (300 seconds) for production, 1 minute for development
- Configuration class will validate required values

### 2. Configuration Class

**File:** `src/Ledger.Api/Configuration/JwtSettings.cs`

```csharp
public class JwtSettings
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int ClockSkewSeconds { get; set; } = 300;
}
```

**Validation:**

- All properties required (non-empty)
- SecretKey minimum length validation
- ClockSkewSeconds range validation (0-3600)

### 3. JWT Authentication Setup

**File:** `src/Ledger.Api/Program.cs`Add JWT authentication:

- Install package: `Microsoft.AspNetCore.Authentication.JwtBearer` (version 8.0.x)
- Configure `AddAuthentication().AddJwtBearer()`
- Set authentication scheme to JwtBearerDefaults.AuthenticationScheme
- Configure TokenValidationParameters:
- ValidateIssuer = true
- ValidateAudience = true
- ValidateLifetime = true
- ValidateIssuerSigningKey = true
- ValidIssuer = from configuration
- ValidAudience = from configuration
- IssuerSigningKey = from SecretKey (SymmetricSecurityKey)
- ClockSkew = TimeSpan.FromSeconds(ClockSkewSeconds)
- Register JwtSettings from configuration
- Add `UseAuthentication()` before `UseAuthorization()`

**Configuration Code:**

```csharp
// Register JWT settings
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();
if (jwtSettings == null || string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
{
    throw new InvalidOperationException("JWT configuration is missing or invalid.");
}
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

// Add JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.FromSeconds(jwtSettings.ClockSkewSeconds)
        };
    });
```



### 4. Global Authorization

**File:** `src/Ledger.Api/Program.cs`Apply authorization globally:

- Add `builder.Services.AddAuthorization()` (if not already present)
- Configure authorization policy (default policy requires authentication)
- Add authorization filter globally or use `[Authorize]` on all controllers

**Approach Options:**

1. **Global Authorization Filter (Recommended):**

- Add `[Authorize]` to all controllers manually
- Or use `builder.Services.AddControllers().AddMvcOptions(options => options.Filters.Add(new AuthorizeFilter()))`

2. **Authorization Policy:**

- Default policy requires authentication
- No role/policy requirements

**Decision:** Use `[Authorize]` attribute on all controllers for explicit control. Health check endpoint should be excluded.

### 5. Authentication Challenge Response

**File:** `src/Ledger.Api/Program.cs` or **File:** `src/Ledger.Api/Middleware/AuthenticationChallengeMiddleware.cs`Configure authentication challenge to return ProblemDetails:

- Override `OnChallenge` event in JWT Bearer options
- Return RFC7807 ProblemDetails with:
- Status: 401
- Title: "Unauthorized"
- Detail: Appropriate message based on failure reason
- reasonCode: "UNAUTHORIZED", "TOKEN_EXPIRED", "TOKEN_INVALID", etc.
- correlationId: from HttpContext.Items

**Implementation:**

```csharp
options.Events = new JwtBearerEvents
{
    OnChallenge = async context =>
    {
        context.HandleResponse();
        
        var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() 
            ?? Guid.NewGuid().ToString();
        
        var problemDetails = new ProblemDetails
        {
            Status = 401,
            Title = "Unauthorized",
            Detail = "Authentication required. Please provide a valid JWT Bearer token.",
            Instance = context.HttpContext.Request.Path,
            Type = "https://tools.ietf.org/html/rfc7235#section-3.1"
        };
        
        problemDetails.Extensions["reasonCode"] = "UNAUTHORIZED";
        problemDetails.Extensions["correlationId"] = correlationId;
        
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/problem+json";
        
        await context.Response.WriteAsJsonAsync(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    },
    OnAuthenticationFailed = context =>
    {
        // Log authentication failures
        return Task.CompletedTask;
    }
};
```



### 6. Controller Authorization

**Files to Update:**

- `src/Ledger.Api/Controllers/AccountsController.cs`
- `src/Ledger.Api/Controllers/JournalEntriesController.cs`
- `src/Ledger.Api/Controllers/ReportsController.cs`

Add `[Authorize]` attribute to all controllers:

```csharp
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
```

**Health Check:**

- Health check endpoint (`/health`) should remain public (no `[Authorize]`)
- Swagger UI in development may need special handling (optional)

### 7. Exception Handling for Authentication

**File:** `src/Ledger.Api/Middleware/ExceptionHandlingMiddleware.cs`Ensure authentication exceptions are handled:

- `SecurityTokenExpiredException` → 401 with reasonCode "TOKEN_EXPIRED"
- `SecurityTokenInvalidSignatureException` → 401 with reasonCode "TOKEN_INVALID_SIGNATURE"
- `SecurityTokenInvalidIssuerException` → 401 with reasonCode "TOKEN_INVALID_ISSUER"
- `SecurityTokenInvalidAudienceException` → 401 with reasonCode "TOKEN_INVALID_AUDIENCE"
- Generic authentication exceptions → 401 with reasonCode "UNAUTHORIZED"

**Note:** Most authentication failures are handled by the challenge event, but exceptions may still occur and should be caught.

### 8. Swagger/OpenAPI Configuration

**File:** `src/Ledger.Api/Program.cs`Configure Swagger to support JWT Bearer:

- Add security definition for Bearer token
- Enable "Authorize" button in Swagger UI
- Allow testing with JWT tokens in development

**Configuration:**

```csharp
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
```



### 9. Unit Tests

**File:** `tests/Ledger.Tests/Api/Middleware/JwtAuthenticationTests.cs`Test scenarios:

- Missing token → 401 Unauthorized
- Invalid token format → 401 Unauthorized
- Expired token → 401 with reasonCode "TOKEN_EXPIRED"
- Invalid signature → 401 with reasonCode "TOKEN_INVALID_SIGNATURE"
- Wrong issuer → 401 with reasonCode "TOKEN_INVALID_ISSUER"
- Wrong audience → 401 with reasonCode "TOKEN_INVALID_AUDIENCE"
- Token not yet valid (nbf) → 401 Unauthorized
- Valid token → 200 OK
- ProblemDetails structure for 401 responses
- CorrelationId included in 401 responses

**Test Approach:**

- Use `WebApplicationFactory` for integration-style tests
- Generate test JWTs with different scenarios
- Use `Microsoft.IdentityModel.Tokens` for token generation in tests

### 10. Integration Tests

**File:** `tests/Ledger.Tests/Integration/Api/AuthenticationTests.cs`Test scenarios using Testcontainers:

- GET /api/accounts without token → 401
- GET /api/accounts with valid token → 200
- GET /api/accounts with expired token → 401
- GET /api/accounts with invalid token → 401
- POST /api/journal-entries without token → 401
- POST /api/journal-entries with valid token → 201
- Verify health check endpoint is public (no auth required)
- Verify correlationId in 401 responses

**Test Token Generation:**

- Create helper method to generate valid test tokens
- Use test JWT settings (different from production)
- Test with various token states (expired, invalid signature, etc.)

### 11. Environment Variable Support

**File:** `src/Ledger.Api/Program.cs`Support environment variables for JWT configuration:

- `JWT__SECRETKEY` (double underscore for nested config)
- `JWT__ISSUER`
- `JWT__AUDIENCE`
- `JWT__CLOCKSKEWSECONDS`

**Priority:**

1. Environment variables (highest)
2. appsettings.{Environment}.json
3. appsettings.json (lowest)

### 12. Documentation Updates

**File:** `docs/VALIDATION_MATRIX.md`Update JWT Authentication section:

- Mark implemented validations as tested
- Add test coverage notes
- Document configuration requirements

**File:** `PROMPTS.md`Add Phase 6 entry documenting:

- JWT configuration approach
- Authentication setup
- Challenge response handling
- Test coverage approach
- Environment variable support

## File Structure

```javascript
src/Ledger.Api/
├── Configuration/
│   └── JwtSettings.cs (new)
├── Controllers/
│   ├── AccountsController.cs (update - add [Authorize])
│   ├── JournalEntriesController.cs (update - add [Authorize])
│   └── ReportsController.cs (update - add [Authorize])
└── Program.cs (update - add JWT authentication)

tests/Ledger.Tests/
├── Api/
│   └── Middleware/
│       └── JwtAuthenticationTests.cs (new)
└── Integration/
    └── Api/
        └── AuthenticationTests.cs (new)
```



## Implementation Details

### JWT Token Validation Parameters

```csharp
new TokenValidationParameters
{
    ValidateIssuer = true,                    // Must match configured Issuer
    ValidateAudience = true,                  // Must match configured Audience
    ValidateLifetime = true,                   // Must not be expired
    ValidateIssuerSigningKey = true,          // Signature must be valid
    ValidIssuer = jwtSettings.Issuer,
    ValidAudience = jwtSettings.Audience,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
    ClockSkew = TimeSpan.FromSeconds(jwtSettings.ClockSkewSeconds), // Allow clock skew
    RequireExpirationTime = true,             // Token must have exp claim
    RequireSignedTokens = true                // Token must be signed
}
```



### Authentication Challenge Response Format

```json
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Authentication required. Please provide a valid JWT Bearer token.",
  "instance": "/api/accounts",
  "reasonCode": "UNAUTHORIZED",
  "correlationId": "guid-here"
}
```



### Test Token Generation Helper

```csharp
public static class TestJwtTokenHelper
{
    public static string GenerateToken(
        string issuer,
        string audience,
        string secretKey,
        DateTime? expires = null,
        DateTime? notBefore = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var claims = new List<Claim>();
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            notBefore: notBefore ?? DateTime.UtcNow,
            signingCredentials: credentials);
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```



### Middleware Order

```javascript
1. CorrelationId
2. ExceptionHandling (catches auth exceptions)
3. HttpsRedirection
4. Authentication (UseAuthentication)
5. Authorization (UseAuthorization)
6. Controllers
```



## Verification Steps

1. All endpoints require authentication (except /health)
2. Missing token returns 401 with ProblemDetails
3. Invalid token returns 401 with ProblemDetails
4. Expired token returns 401 with reasonCode "TOKEN_EXPIRED"
5. Valid token grants access to all endpoints
6. CorrelationId included in all 401 responses
7. JWT configuration from environment variables works
8. Clock skew is configurable
9. Swagger UI supports JWT Bearer token input
10. Unit tests pass
11. Integration tests pass
12. Documentation updated

## Critical Considerations

- **No Hardcoded Secrets:** All secrets must come from configuration/environment
- **Clock Skew:** Must be explicitly configured (default 5 minutes)
- **Global Authorization:** All endpoints protected by default
- **Health Check:** Should remain public (no authentication)
- **ProblemDetails:** All 401 responses must use RFC7807 format
- **CorrelationId:** Must be included in all error responses