---
name: Accounts API Implementation
overview: Implement full CRUD API for Accounts with proper validation, duplicate prevention, and business rule enforcement. Include unit and integration tests using PostgreSQL Testcontainers, following Clean Architecture principles.
todos:
  - id: create-dtos
    content: "Create DTOs: CreateAccountRequest, UpdateAccountRequest, AccountResponse with proper validation attributes"
    status: completed
  - id: create-account-service-interface
    content: Create IAccountService interface with all CRUD operations and helper methods
    status: completed
  - id: implement-account-service
    content: Implement AccountService with validation, duplicate checking, type immutability checks, and database operations
    status: completed
    dependencies:
      - create-account-service-interface
  - id: register-dbcontext-in-application
    content: Register DbContext for use in Application layer (or create repository abstraction)
    status: pending
  - id: create-accounts-controller
    content: Create AccountsController with POST, GET (list), GET (by id), and PUT endpoints
    status: completed
    dependencies:
      - create-dtos
      - implement-account-service
  - id: add-testcontainers-package
    content: Add Testcontainers.PostgreSql package to test project
    status: completed
  - id: create-unit-tests
    content: Create unit tests for AccountService covering all validation scenarios and business rules
    status: completed
    dependencies:
      - implement-account-service
  - id: create-integration-tests
    content: Create integration tests for AccountsController using Testcontainers with PostgreSQL
    status: completed
    dependencies:
      - create-accounts-controller
      - add-testcontainers-package
  - id: update-validation-matrix
    content: Update VALIDATION_MATRIX.md to reflect implemented and tested validations
    status: completed
    dependencies:
      - create-integration-tests
  - id: update-prompts
    content: Update PROMPTS.md with Phase 3 Accounts API implementation decisions
    status: completed
    dependencies:
      - create-integration-tests
---

# Accounts API Implementation Plan

## Overview

Implement the Accounts API with full CRUD operations, following Clean Architecture principles. All endpoints must enforce validation rules, handle duplicates, and prevent type changes after account usage. Include comprehensive test coverage.

## Current State

- Domain entity `Account` exists with immutability patterns
- EF Core configuration with case-insensitive unique index
- Exception handling middleware in place
- Custom exceptions (ValidationException, ConflictException, NotFoundException)
- No controllers, DTOs, or application services yet

## Implementation Tasks

### 1. DTOs (Data Transfer Objects)

**File:** `src/Ledger.Api/DTOs/Accounts/CreateAccountRequest.cs`

```csharp
public record CreateAccountRequest(
    [Required, MaxLength(200)] string Name,
    [Required] AccountType Type,
    bool IsActive = true
);
```

**File:** `src/Ledger.Api/DTOs/Accounts/UpdateAccountRequest.cs`

```csharp
public record UpdateAccountRequest(
    bool IsActive
);
```

**File:** `src/Ledger.Api/DTOs/Accounts/AccountResponse.cs`

```csharp
public record AccountResponse(
    Guid Id,
    string Name,
    AccountType Type,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
```

**Design Decisions:**

- Use records for DTOs (immutability)
- Data annotations for validation
- Name trimming handled in Application layer
- Update only supports IsActive (type is immutable after usage)

### 2. Application Layer Services

**File:** `src/Ledger.Application/Services/IAccountService.cs`Interface defining operations:

- `Task<Account> CreateAccountAsync(string name, AccountType type, bool isActive, CancellationToken cancellationToken)`
- `Task<Account?> GetAccountByIdAsync(Guid id, CancellationToken cancellationToken)`
- `Task<IReadOnlyList<Account>> GetAllAccountsAsync(CancellationToken cancellationToken)`
- `Task<Account> UpdateAccountIsActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)`
- `Task<bool> AccountHasJournalLinesAsync(Guid accountId, CancellationToken cancellationToken)`

**File:** `src/Ledger.Application/Services/AccountService.cs`Implementation with:

- Validation logic (name trimming, length checks)
- Duplicate name checking (case-insensitive)
- Type immutability check (prevent change if account has journal lines)
- Uses DbContext via repository pattern or direct injection
- Throws custom exceptions (ValidationException, ConflictException, NotFoundException)

**Validation Rules:**

- Name: required, trimmed, max 200 characters, not empty after trim
- Type: valid enum value
- Duplicate name: case-insensitive check → ConflictException (409)
- Type change prevention: check if account has journal lines → ValidationException (400)

### 3. Repository Pattern (Optional)

**Decision:** Use direct DbContext injection in Application services (simpler for this use case) or create repository interface.**Approach:** Direct DbContext injection in Application layer (Infrastructure provides DbContext, Application uses it)

### 4. Controller Implementation

**File:** `src/Ledger.Api/Controllers/AccountsController.cs`Endpoints:

- `POST /api/accounts` - Create account
- Validates DTO
- Calls AccountService.CreateAccountAsync
- Returns 201 Created with AccountResponse
- Handles ConflictException → 409
- Handles ValidationException → 400
- `GET /api/accounts` - List all accounts
- Calls AccountService.GetAllAccountsAsync
- Returns 200 OK with list of AccountResponse
- `GET /api/accounts/{id}` - Get account by ID
- Calls AccountService.GetAccountByIdAsync
- Returns 200 OK with AccountResponse
- Handles NotFoundException → 404
- `PUT /api/accounts/{id}` - Update account (IsActive only)
- Validates DTO
- Calls AccountService.UpdateAccountIsActiveAsync
- Returns 200 OK with AccountResponse
- Handles NotFoundException → 404
- Handles ValidationException → 400 (if type change attempted)

**Controller Design:**

- No business logic (delegates to Application layer)
- Uses [FromBody] and [FromRoute] attributes
- Returns appropriate HTTP status codes
- Exception handling via middleware (automatic)

### 5. Integration with DbContext

**File:** `src/Ledger.Application/Services/AccountService.cs`

- Inject `LedgerDbContext` (via interface or direct)
- Use async/await for all database operations
- Handle DbUpdateException for duplicate detection
- Use transactions where needed
- Set CreatedAt/UpdatedAt timestamps (UTC)

**Timestamp Handling:**

- CreatedAt: set on creation (UTC)
- UpdatedAt: set on update (UTC)
- Use `DateTime.UtcNow` (not `DateTime.Now`)

### 6. Duplicate Name Detection

**Strategy:**

1. Application layer: Check for existing account with same name (case-insensitive) before insert
2. Database: Unique index (UPPER(Name)) as backstop
3. Handle DbUpdateException → ConflictException with reasonCode "DUPLICATE_ACCOUNT_NAME"

**Implementation:**

```csharp
var existing = await _context.Accounts
    .FirstOrDefaultAsync(a => a.Name.ToUpper() == name.ToUpper(), cancellationToken);
if (existing != null)
{
    throw new ConflictException("An account with this name already exists", "DUPLICATE_ACCOUNT_NAME");
}
```



### 7. Type Immutability Check

**File:** `src/Ledger.Application/Services/AccountService.cs`Check if account has journal lines before allowing type change:

```csharp
var hasLines = await _context.JournalEntryLines
    .AnyAsync(l => l.AccountId == accountId, cancellationToken);
if (hasLines)
{
    throw new ValidationException("Account type cannot be changed after journal entries are posted", "ACCOUNT_TYPE_IMMUTABLE");
}
```

**Note:** PUT endpoint only allows IsActive changes, but validation should still check for type change attempts.

### 8. Unit Tests

**File:** `tests/Ledger.Tests/Application/Services/AccountServiceTests.cs`Test scenarios:

- CreateAccountAsync: success case
- CreateAccountAsync: duplicate name (case-insensitive)
- CreateAccountAsync: invalid name (empty, too long)
- CreateAccountAsync: invalid AccountType
- GetAccountByIdAsync: found and not found
- GetAllAccountsAsync: returns all accounts
- UpdateAccountIsActiveAsync: success case
- UpdateAccountIsActiveAsync: account not found
- AccountHasJournalLinesAsync: true and false cases

**Mocking:**

- Mock DbContext or use in-memory database
- Test validation logic
- Test exception throwing

### 9. Integration Tests (PostgreSQL Testcontainers)

**File:** `tests/Ledger.Tests/Integration/Api/AccountsControllerTests.cs`**Setup:**

- Use Testcontainers.PostgreSql
- Create test database
- Run migrations
- Seed test data

**Test Scenarios:**

- POST /api/accounts: create account successfully
- POST /api/accounts: duplicate name (case-insensitive) → 409
- POST /api/accounts: invalid name → 400
- GET /api/accounts: returns all accounts
- GET /api/accounts/{id}: returns account
- GET /api/accounts/{id}: not found → 404
- PUT /api/accounts/{id}: update IsActive successfully
- PUT /api/accounts/{id}: not found → 404
- Verify correlationId in all responses
- Verify reasonCode in error responses

**Testcontainers Setup:**

- Add package: `Testcontainers.PostgreSql`
- Create test base class for database setup
- Run migrations in test setup
- Clean up after tests

### 10. Dependencies

**Application Layer:**

- Reference Infrastructure for DbContext access
- Use custom exceptions from Application.Exceptions

**API Layer:**

- Reference Application for services
- Use DTOs for request/response
- Controllers delegate to Application services

**Tests:**

- Testcontainers.PostgreSql package
- Moq (already added)
- xUnit (already added)

### 11. Documentation Updates

**File:** `PROMPTS.md`Add Phase 3 entry documenting:

- DTO design decisions
- Service layer implementation
- Validation rules
- Duplicate prevention strategy
- Type immutability enforcement
- Test coverage approach

**File:** `docs/VALIDATION_MATRIX.md`Update Accounts API section:

- Mark implemented validations as tested
- Add integration test coverage notes
- Document API endpoint behaviors

## File Structure

```javascript
src/Ledger.Api/
├── Controllers/
│   └── AccountsController.cs (new)
└── DTOs/
    └── Accounts/
        ├── CreateAccountRequest.cs (new)
        ├── UpdateAccountRequest.cs (new)
        └── AccountResponse.cs (new)

src/Ledger.Application/
└── Services/
    ├── IAccountService.cs (new)
    └── AccountService.cs (new)

tests/Ledger.Tests/
├── Application/
│   └── Services/
│       └── AccountServiceTests.cs (new)
└── Integration/
    └── Api/
        └── AccountsControllerTests.cs (new)
```



## Implementation Details

### Name Validation

- Trim whitespace: `name = name.Trim()`
- Check length: max 200 characters (enforced by DB and DTO)
- Check not empty after trim
- Case-insensitive duplicate check

### Account Type Immutability

- Check if account has any JournalEntryLines
- If yes, reject type change (even though PUT only allows IsActive changes, validate defensively)
- Return ValidationException with reasonCode "ACCOUNT_TYPE_IMMUTABLE"

### Error Responses

All errors use ProblemDetails format:

- 400: Validation errors (INVALID_NAME, INVALID_ACCOUNT_TYPE, ACCOUNT_TYPE_IMMUTABLE)
- 404: Account not found (ACCOUNT_NOT_FOUND)
- 409: Duplicate name (DUPLICATE_ACCOUNT_NAME)

### Timestamp Management

- CreatedAt: Set to `DateTime.UtcNow` on creation
- UpdatedAt: Set to `DateTime.UtcNow` on update
- Database defaults also set (backstop)

## Verification Steps

1. All endpoints compile and run
2. Unit tests pass
3. Integration tests pass with Testcontainers
4. Duplicate name detection works (case-insensitive)
5. Type immutability enforced
6. Validation errors return 400 with reasonCode
7. Duplicate errors return 409 with reasonCode