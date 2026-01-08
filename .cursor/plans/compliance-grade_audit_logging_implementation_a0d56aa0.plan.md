---
name: Compliance-Grade Audit Logging Implementation
overview: Implement append-only audit logging using EF Core SaveChangesInterceptor to capture all entity changes (Account, JournalEntry, User) and login events. Audit logs stored as JSON with sensitive field exclusion, written in same transaction, and transaction fails if audit logging fails.
todos:
  - id: create-auditlog-entity
    content: Create AuditLog domain entity with EntityName, EntityId, Action, OldValues, NewValues, PerformedBy, CorrelationId, Timestamp
    status: in_progress
  - id: create-auditlog-ef-config
    content: Create EF Core AuditLogConfiguration with JSONB columns for OldValues/NewValues
    status: pending
    dependencies:
      - create-auditlog-entity
  - id: create-auditlog-migration
    content: Generate database migration for AuditLogs table
    status: pending
    dependencies:
      - create-auditlog-ef-config
  - id: create-user-context-service
    content: Create IUserContextService and UserContextService to extract userId from JWT claims
    status: pending
  - id: create-sensitive-field-excluder
    content: Create utility to exclude sensitive fields (PasswordHash, Secret, etc.) from JSON serialization
    status: pending
  - id: create-auditlog-repository
    content: Create IAuditLogRepository interface and AuditLogRepository implementation
    status: pending
    dependencies:
      - create-auditlog-entity
  - id: create-auditlog-service
    content: Create IAuditLogService and AuditLogService with sensitive field exclusion
    status: pending
    dependencies:
      - create-auditlog-repository
      - create-sensitive-field-excluder
  - id: create-savechanges-interceptor
    content: Create AuditLoggingInterceptor (SaveChangesInterceptor) to capture entity changes and create audit logs
    status: pending
    dependencies:
      - create-auditlog-service
      - create-user-context-service
  - id: update-dbcontext
    content: Add DbSet<AuditLog> to LedgerDbContext
    status: pending
    dependencies:
      - create-auditlog-entity
  - id: register-services
    content: Register HttpContextAccessor, audit services, and interceptor in Program.cs
    status: pending
    dependencies:
      - create-savechanges-interceptor
  - id: update-authentication-service
    content: Add login audit logging to AuthenticationService
    status: pending
    dependencies:
      - create-auditlog-service
  - id: create-unit-tests
    content: Create unit tests for AuditLogService, UserContextService, and AuditLoggingInterceptor
    status: pending
    dependencies:
      - create-savechanges-interceptor
  - id: create-integration-tests
    content: Create integration tests for audit logging (Account, JournalEntry, User, Login)
    status: pending
    dependencies:
      - register-services
      - update-authentication-service
  - id: update-docs
    content: Update PROMPTS.md and VALIDATION_MATRIX.md with audit logging implementation details
    status: pending
    dependencies:
      - create-integration-tests
---

# Compliance-Grade Audit Logging Implementation Plan

## Overview

Implement append-only audit logging using EF Core `SaveChangesInterceptor` to automatically capture all entity changes. Audit logs are immutable, stored as JSON, exclude sensitive fields, and are written in the same transaction as the business operation. If audit logging fails, the transaction must fail.

## Current State

- **CorrelationId**: Available in `HttpContext.Items["CorrelationId"] `via `CorrelationIdMiddleware`
- **User Context**: JWT authentication is in place, but no current user extraction mechanism
- **SaveChanges**: Called in repositories (AccountRepository, UserRepository, JournalEntryRepository)
- **No Audit Logging**: Currently no audit trail exists

## Implementation Tasks

### 1. AuditLog Domain Entity

**File:** `src/Ledger.Domain/Entities/AuditLog.cs`Create immutable audit log entity:

```csharp
public class AuditLog
{
    public Guid Id { get; init; }
    public string EntityName { get; init; } // e.g., "Account", "JournalEntry", "User"
    public Guid EntityId { get; init; }
    public string Action { get; init; } // "CREATE", "UPDATE", "LOGIN"
    public string? OldValues { get; init; } // JSON, null for CREATE
    public string? NewValues { get; init; } // JSON, null for DELETE (not applicable here)
    public Guid? PerformedBy { get; init; } // UserId, nullable for system operations
    public string CorrelationId { get; init; }
    public DateTime Timestamp { get; init; } // UTC
}
```

**Design Decisions:**

- Not inheriting from BaseEntity (audit logs are append-only, no updates)
- Id is Guid for consistency
- OldValues/NewValues as JSON strings (PostgreSQL JSONB or TEXT)
- PerformedBy nullable (system operations may not have a user)
- Action as string enum-like values (CREATE, UPDATE, LOGIN)

### 2. EF Core Configuration for AuditLog

**File:** `src/Ledger.Infrastructure/Data/Configurations/AuditLogConfiguration.cs`Configure AuditLog entity:

- Id: primary key, Guid
- EntityName: required, max length 100
- EntityId: required, Guid
- Action: required, max length 50
- OldValues: nullable, JSONB or TEXT (PostgreSQL)
- NewValues: nullable, JSONB or TEXT
- PerformedBy: nullable, Guid
- CorrelationId: required, max length 100
- Timestamp: required, UTC timestamp with default CURRENT_TIMESTAMP
- Table name: "AuditLogs"
- No indexes initially (can add later for querying)

### 3. Database Migration

**Command:** `dotnet ef migrations add AddAuditLogsTable`**File:** `src/Ledger.Infrastructure/Migrations/YYYYMMDDHHMMSS_AddAuditLogsTable.cs`Migration will:

- Create AuditLogs table
- Use JSONB for OldValues/NewValues (PostgreSQL native JSON support)
- Set Timestamp default to CURRENT_TIMESTAMP
- Add comment: "Append-only audit log table. Immutable."

### 4. Audit Logging Service Interface

**File:** `src/Ledger.Application/Services/IAuditLogService.cs`

```csharp
public interface IAuditLogService
{
    Task LogEntityChangeAsync(
        string entityName,
        Guid entityId,
        string action,
        object? oldValues,
        object? newValues,
        Guid? performedBy,
        string correlationId,
        CancellationToken cancellationToken = default);
}
```

**Design Decisions:**

- Service interface in Application layer (business logic)
- Accepts objects for old/new values (will serialize to JSON)
- PerformedBy nullable

### 5. Audit Logging Service Implementation

**File:** `src/Ledger.Application/Services/AuditLogService.cs`Implementation:

- Serialize old/new values to JSON
- Exclude sensitive fields (PasswordHash, Secret, etc.)
- Call repository to save audit log

**Sensitive Field Exclusion:**

- Create list of sensitive field names: `PasswordHash`, `Password`, `Secret`, `SecretKey`, `Token`
- Use JSON serialization with custom contract resolver or manual filtering
- Option: Use `System.Text.Json` with custom `JsonSerializerOptions` and attribute-based exclusion

### 6. EF Core SaveChangesInterceptor

**File:** `src/Ledger.Infrastructure/Data/Interceptors/AuditLoggingInterceptor.cs`Create interceptor that:

- Implements `SaveChangesInterceptor` (EF Core 7+)
- Overrides `SavingChangesAsync` to capture changes before save
- Extracts correlationId from `IHttpContextAccessor`
- Extracts userId from JWT claims via `IHttpContextAccessor`
- Detects entity changes (Added, Modified, Deleted)
- Serializes old/new values (excluding sensitive fields)
- Creates AuditLog entries
- Adds AuditLog entries to context (same transaction)

**Key Implementation Details:**

```csharp
public class AuditLoggingInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuditLogService _auditLogService;
    
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        // Capture changes and create audit logs
        // Add audit logs to context
        return base.SavingChanges(eventData, result);
    }
}
```

**Entity Change Detection:**

- `EntityState.Added` → Action = "CREATE", OldValues = null
- `EntityState.Modified` → Action = "UPDATE", capture old and new values
- `EntityState.Deleted` → Not applicable (no hard deletes per rules)

**Entities to Audit:**

- Account (CREATE, UPDATE)
- JournalEntry (CREATE - posting)
- User (CREATE, UPDATE - for enable/disable, failed attempts)
- Login events (special case - not an entity change)

### 7. Login Audit Logging

**File:** `src/Ledger.Application/Services/AuthenticationService.cs`Add audit logging for login:

- After successful login, create audit log entry
- Action = "LOGIN"
- EntityName = "User"
- EntityId = userId
- OldValues = null
- NewValues = JSON with LastLoginAt timestamp
- PerformedBy = userId (self)
- CorrelationId from HttpContext

**Design Decision:**

- Login is not an entity change, so handled separately in AuthenticationService
- Use IAuditLogService directly in AuthenticationService

### 8. Audit Log Repository

**File:** `src/Ledger.Application/Repositories/IAuditLogRepository.cs`**File:** `src/Ledger.Infrastructure/Repositories/AuditLogRepository.cs`Repository for audit logs:

- `AddAsync(AuditLog auditLog)` - Add single audit log
- `AddRangeAsync(IEnumerable<AuditLog> auditLogs)` - Add multiple (for batch operations)

**Design Decision:**

- Repository pattern for consistency
- Used by AuditLogService

### 9. User Context Extraction

**File:** `src/Ledger.Infrastructure/Data/Services/IUserContextService.cs`**File:** `src/Ledger.Infrastructure/Data/Services/UserContextService.cs`Service to extract current user from HttpContext:

- Read JWT claims from `HttpContext.User`
- Extract `sub` claim (userId)
- Return `Guid?` (nullable if no user)

**Implementation:**

```csharp
public class UserContextService : IUserContextService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public Guid? GetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User
            .FindFirst(ClaimTypes.NameIdentifier) // or "sub"
            ?.Value;
        
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
```



### 10. Sensitive Field Exclusion

**File:** `src/Ledger.Application/Services/SensitiveFieldExcluder.cs`Utility to exclude sensitive fields from JSON serialization:

- List of sensitive field names
- Method to serialize object excluding sensitive fields
- Use `System.Text.Json` with custom `JsonSerializerOptions`

**Implementation Options:**

1. **Custom JsonConverter**: Create converter that skips sensitive properties
2. **Manual Dictionary Building**: Build dictionary manually, excluding sensitive keys
3. **Attribute-based**: Use `[JsonIgnore]` attribute (requires domain changes - not preferred)

**Preferred Approach:** Manual dictionary building for flexibility:

```csharp
public static class SensitiveFieldExcluder
{
    private static readonly HashSet<string> SensitiveFields = new()
    {
        "PasswordHash", "Password", "Secret", "SecretKey", "Token", "ApiKey"
    };
    
    public static Dictionary<string, object?> SerializeExcludingSensitive(object? obj)
    {
        if (obj == null) return new Dictionary<string, object?>();
        
        var json = JsonSerializer.Serialize(obj);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        
        return dict?.Where(kvp => !SensitiveFields.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value) 
            ?? new Dictionary<string, object?>();
    }
}
```



### 11. DbContext Updates

**File:** `src/Ledger.Infrastructure/Data/LedgerDbContext.cs`Add:

```csharp
public DbSet<AuditLog> AuditLogs { get; set; } = null!;
```



### 12. Dependency Injection

**File:** `src/Ledger.Api/Program.cs`Register services:

```csharp
// Add HttpContextAccessor for audit logging
builder.Services.AddHttpContextAccessor();

// Register audit logging services
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// Register EF Core interceptor
builder.Services.AddDbContext<LedgerDbContext>((sp, options) =>
{
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var auditLogService = sp.GetRequiredService<IAuditLogService>();
    options.AddInterceptors(new AuditLoggingInterceptor(httpContextAccessor, auditLogService));
    // ... existing configuration
});
```

**Design Decision:**

- Interceptor registered at DbContext level
- Services resolved from DI in interceptor constructor
- HttpContextAccessor required for correlationId and userId

### 13. Update AuthenticationService for Login Audit

**File:** `src/Ledger.Application/Services/AuthenticationService.cs`Inject `IAuditLogService` and `IHttpContextAccessor`:

- After successful login, create audit log
- Action = "LOGIN"
- EntityName = "User"
- EntityId = user.Id
- NewValues = JSON with LastLoginAt
- PerformedBy = user.Id
- CorrelationId from HttpContext

### 14. Unit Tests

**File:** `tests/Ledger.Tests/Application/Services/AuditLogServiceTests.cs`

- SerializeExcludingSensitive excludes password fields
- SerializeExcludingSensitive excludes secret fields
- SerializeExcludingSensitive preserves non-sensitive fields
- LogEntityChangeAsync creates audit log with correct values

**File:** `tests/Ledger.Tests/Infrastructure/Data/Services/UserContextServiceTests.cs`

- GetCurrentUserId returns userId from JWT claims
- GetCurrentUserId returns null when no user
- GetCurrentUserId returns null when claim invalid

**File:** `tests/Ledger.Tests/Infrastructure/Data/Interceptors/AuditLoggingInterceptorTests.cs`

- SavingChangesAsync captures Added entities
- SavingChangesAsync captures Modified entities
- SavingChangesAsync excludes sensitive fields
- SavingChangesAsync includes correlationId
- SavingChangesAsync includes userId when available
- SavingChangesAsync writes audit logs in same transaction

### 15. Integration Tests

**File:** `tests/Ledger.Tests/Integration/Api/AuditLoggingTests.cs`

- Account creation creates audit log
- Account update creates audit log
- Journal entry posting creates audit log
- User creation creates audit log
- Login creates audit log
- Audit logs exclude password hash
- Audit logs include correlationId
- Audit logs include userId
- Audit log failure causes transaction rollback

### 16. Documentation Updates

**File:** `PROMPTS.md`Add Phase 8 entry documenting:

- Audit logging architecture (EF Core interceptor)
- Sensitive field exclusion
- Transaction safety
- Login audit logging
- User context extraction

**File:** `docs/VALIDATION_MATRIX.md`Update AUDIT LOGGING section:

- Mark all audit logging validations as implemented
- Add test coverage status

## File Structure

```javascript
src/Ledger.Domain/
├── Entities/
│   └── AuditLog.cs (new)

src/Ledger.Application/
├── Repositories/
│   └── IAuditLogRepository.cs (new)
├── Services/
│   ├── IAuditLogService.cs (new)
│   └── AuditLogService.cs (new)

src/Ledger.Infrastructure/
├── Data/
│   ├── Configurations/
│   │   └── AuditLogConfiguration.cs (new)
│   ├── Interceptors/
│   │   └── AuditLoggingInterceptor.cs (new)
│   ├── Services/
│   │   ├── IUserContextService.cs (new)
│   │   └── UserContextService.cs (new)
│   └── LedgerDbContext.cs (update)
├── Repositories/
│   └── AuditLogRepository.cs (new)
└── Migrations/
    └── YYYYMMDDHHMMSS_AddAuditLogsTable.cs (new)

src/Ledger.Application/
└── Services/
    └── AuthenticationService.cs (update - add login audit)

src/Ledger.Api/
└── Program.cs (update - register services and interceptor)

tests/Ledger.Tests/
├── Application/
│   └── Services/
│       └── AuditLogServiceTests.cs (new)
├── Infrastructure/
│   ├── Data/
│   │   ├── Interceptors/
│   │   │   └── AuditLoggingInterceptorTests.cs (new)
│   │   └── Services/
│   │       └── UserContextServiceTests.cs (new)
└── Integration/
    └── Api/
        └── AuditLoggingTests.cs (new)
```



## Critical Implementation Details

### 1. Transaction Safety

- Audit logs MUST be written in the same transaction
- If audit logging fails, the entire transaction MUST rollback
- Use EF Core's built-in transaction (SaveChanges is atomic)
- Add audit logs to context before calling SaveChanges

### 2. Sensitive Field Exclusion

- Exclude: PasswordHash, Password, Secret, SecretKey, Token, ApiKey
- Case-insensitive matching
- Apply to both OldValues and NewValues
- Preserve all other fields

### 3. User Context

- Extract userId from JWT `sub` claim
- Nullable (system operations may not have user)
- Use IHttpContextAccessor to access HttpContext
- Handle cases where HttpContext is null (background jobs)

### 4. CorrelationId

- Extract from HttpContext.Items["CorrelationId"]
- Always available (CorrelationIdMiddleware ensures it)
- Fallback to new GUID if missing (defensive)

### 5. Entity Change Detection

- Track Added entities (CREATE)
- Track Modified entities (UPDATE) - capture old and new values
- Ignore Deleted entities (no hard deletes per rules)
- Ignore Unchanged entities

### 6. Login Audit

- Special case: not an entity change
- Handled in AuthenticationService
- Action = "LOGIN"
- EntityName = "User"
- EntityId = userId
- NewValues = JSON with LastLoginAt

## Verification Steps

1. AuditLog entity created with all required fields
2. Database migration creates AuditLogs table with JSONB columns
3. SaveChangesInterceptor captures entity changes
4. Sensitive fields excluded from audit logs
5. CorrelationId included in all audit logs
6. UserId included when available
7. Login events audited
8. Transaction safety verified (audit failure = rollback)
9. All tests pass
10. Documentation updated

## Security Considerations

- **Immutable Audit Logs**: Append-only, no updates or deletes