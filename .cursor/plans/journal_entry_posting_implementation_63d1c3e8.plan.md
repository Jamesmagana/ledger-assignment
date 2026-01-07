---
name: Journal Entry Posting Implementation
overview: Implement Journal Entry posting API with full double-entry validation, idempotency using externalId + SHA-256 request hash, atomic transactions, and comprehensive test coverage including concurrency tests.
todos:
  - id: create-journal-entry-dtos
    content: "Create DTOs: CreateJournalEntryRequest, CreateJournalEntryLineRequest, JournalEntryResponse, JournalEntryLineResponse with validation attributes"
    status: completed
  - id: create-request-hash-service
    content: Create IRequestHashService and RequestHashService for SHA-256 canonical request hashing
    status: completed
  - id: create-journal-entry-repository
    content: Create IJournalEntryRepository interface and JournalEntryRepository implementation with atomic transaction support
    status: completed
  - id: create-journal-entry-service
    content: Create IJournalEntryService and JournalEntryService with all validation rules, balance checking, and idempotency logic
    status: completed
    dependencies:
      - create-request-hash-service
      - create-journal-entry-repository
  - id: register-services
    content: Register RequestHashService, JournalEntryRepository, and JournalEntryService in Program.cs dependency injection
    status: completed
    dependencies:
      - create-journal-entry-service
  - id: create-journal-entries-controller
    content: Create JournalEntriesController with POST and GET endpoints, handling idempotent replays (200 OK)
    status: completed
    dependencies:
      - create-journal-entry-service
  - id: create-unit-tests-service
    content: Create unit tests for JournalEntryService covering all validation scenarios, balance checks, and idempotency
    status: completed
    dependencies:
      - create-journal-entry-service
  - id: create-unit-tests-hash
    content: Create unit tests for RequestHashService verifying deterministic hashing and canonical format
    status: completed
    dependencies:
      - create-request-hash-service
  - id: create-integration-tests
    content: Create integration tests for JournalEntriesController using Testcontainers, covering all endpoints and error scenarios
    status: completed
    dependencies:
      - create-journal-entries-controller
  - id: create-concurrency-test
    content: Create concurrency test to verify safe behavior under parallel requests with same ExternalId
    status: completed
    dependencies:
      - create-integration-tests
  - id: update-validation-matrix
    content: Update VALIDATION_MATRIX.md to reflect implemented and tested journal entry validations
    status: completed
    dependencies:
      - create-concurrency-test
  - id: update-prompts
    content: Update PROMPTS.md with Phase 4 Journal Entry posting implementation decisions
    status: completed
    dependencies:
      - create-concurrency-test
---

# Journal

Entry Posting Implementation Plan

## Overview

Implement the Journal Entry posting API with strict double-entry accounting rules, idempotency support, and atomic transaction handling. All ledger invariants must be enforced, and the system must be safe under concurrent requests.

## Current State

- Domain entities `JournalEntry` and `JournalEntryLine` exist
- Database constraints in place (unique index on ExternalId, CHECK constraint on Amount > 0)
- EF Core configurations exist
- Account service and repository patterns established
- Exception handling middleware in place
- No journal entry DTOs, services, or controllers yet

## Implementation Tasks

### 1. DTOs (Data Transfer Objects)

**File:** `src/Ledger.Api/DTOs/JournalEntries/CreateJournalEntryLineRequest.cs`

```csharp
public record CreateJournalEntryLineRequest(
    [Required] Guid AccountId,
    [Required] LineDirection Direction,
    [Required, Range(0.0001, double.MaxValue)] decimal Amount
);
```

**File:** `src/Ledger.Api/DTOs/JournalEntries/CreateJournalEntryRequest.cs`

```csharp
public record CreateJournalEntryRequest(
    string? ExternalId,
    [Required, MinLength(2)] IReadOnlyList<CreateJournalEntryLineRequest> Lines
);
```

**File:** `src/Ledger.Api/DTOs/JournalEntries/JournalEntryLineResponse.cs`

```csharp
public record JournalEntryLineResponse(
    Guid Id,
    Guid AccountId,
    LineDirection Direction,
    decimal Amount
);
```

**File:** `src/Ledger.Api/DTOs/JournalEntries/JournalEntryResponse.cs`

```csharp
public record JournalEntryResponse(
    Guid Id,
    string? ExternalId,
    DateTime PostedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<JournalEntryLineResponse> Lines,
    bool IdempotencyReplay = false
);
```

**Design Decisions:**

- ExternalId is optional (nullable)
- Lines must have at least 2 items (MinLength validation)
- Amount validation at DTO level (Range attribute)
- IdempotencyReplay flag in response for idempotent replays

### 2. Request Hash Computation

**File:** `src/Ledger.Application/Services/IRequestHashService.cs`

```csharp
public interface IRequestHashService
{
    string ComputeHash(CreateJournalEntryRequest request);
}
```

**File:** `src/Ledger.Application/Services/RequestHashService.cs`Implementation:

- Serialize request to canonical JSON (sorted properties, no whitespace)
- Compute SHA-256 hash
- Return hex string (64 characters)
- Must be deterministic (same input → same hash)

**Canonical Format:**

- Sort lines by AccountId, then Direction, then Amount
- Remove null/empty ExternalId from hash computation
- Consistent property ordering
- No extra whitespace

### 3. Repository Pattern

**File:** `src/Ledger.Application/Repositories/IJournalEntryRepository.cs`Interface methods:

- `Task<JournalEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)`
- `Task<JournalEntry?> FindByExternalIdAsync(string externalId, CancellationToken cancellationToken = default)`
- `Task<(JournalEntry Entry, IReadOnlyList<JournalEntryLine> Lines)> AddWithLinesAsync(JournalEntry entry, IReadOnlyList<JournalEntryLine> lines, CancellationToken cancellationToken = default)`
- `Task<IReadOnlyList<JournalEntryLine>> GetLinesByJournalEntryIdAsync(Guid journalEntryId, CancellationToken cancellationToken = default)`

**File:** `src/Ledger.Infrastructure/Repositories/JournalEntryRepository.cs`Implementation:

- Uses DbContext for all operations
- `AddWithLinesAsync` uses EF Core transaction (SaveChangesAsync)
- `FindByExternalIdAsync` for idempotency checks
- Load lines with entry in `GetByIdAsync`

### 4. Service Layer

**File:** `src/Ledger.Application/Services/IJournalEntryService.cs`Interface methods:

- `Task<JournalEntryResult> PostJournalEntryAsync(CreateJournalEntryRequest request, CancellationToken cancellationToken = default)`
- `Task<JournalEntryResult?> GetJournalEntryByIdAsync(Guid id, CancellationToken cancellationToken = default)`

**File:** `src/Ledger.Application/Services/JournalEntryResult.cs`

```csharp
public record JournalEntryResult(
    JournalEntry Entry,
    IReadOnlyList<JournalEntryLine> Lines,
    bool IsIdempotencyReplay = false
);
```

**File:** `src/Ledger.Application/Services/JournalEntryService.cs`Validation Rules (in order):

1. **Line Count:** Must have at least 2 lines
2. **Amount Validation:**

- Each amount > 0
- Each amount scale ≤ 4 decimal places

3. **Direction Validation:** Each line must have valid LineDirection enum
4. **Account Validation:**

- All AccountIds must exist
- All accounts must be active

5. **Balance Validation:**

- Total Debits == Total Credits (exact decimal match, no rounding)
- Use decimal comparison (no tolerance)

**Idempotency Logic:**

1. If ExternalId provided:

- Compute request hash
- Check for existing entry with same ExternalId
- If found:
    - Compare request hashes
    - Same hash → return existing entry (200 OK, IsIdempotencyReplay = true)
    - Different hash → throw ConflictException (409)
- If not found:
    - Proceed with validation and creation

2. If ExternalId not provided:

- Proceed with validation and creation (no idempotency)

**Atomic Transaction:**

- Use DbContext transaction (BeginTransactionAsync)
- Create JournalEntry
- Create all JournalEntryLines
- SaveChangesAsync (single transaction)
- Commit transaction
- On any error, rollback transaction

**Error Codes:**

- `INVALID_LINE_COUNT` - Less than 2 lines
- `INVALID_AMOUNT` - Amount <= 0 or invalid
- `INVALID_AMOUNT_SCALE` - More than 4 decimal places
- `INVALID_DIRECTION` - Invalid LineDirection enum
- `ACCOUNT_NOT_FOUND` - Account doesn't exist
- `ACCOUNT_INACTIVE` - Account is not active
- `UNBALANCED_ENTRY` - Debits != Credits
- `DUPLICATE_EXTERNAL_ID` - Same ExternalId, different hash (409)
- `IDEMPOTENCY_REPLAY` - Same ExternalId, same hash (200, but flagged)

### 5. Controller Implementation

**File:** `src/Ledger.Api/Controllers/JournalEntriesController.cs`**POST /api/journal-entries**

- Accepts `CreateJournalEntryRequest`
- Calls `JournalEntryService.PostJournalEntryAsync`
- Returns:
- 201 Created for new entries
- 200 OK for idempotent replays (with IdempotencyReplay = true)
- 400 Bad Request for validation errors
- 409 Conflict for duplicate ExternalId with different hash
- Maps `JournalEntryResult` to `JournalEntryResponse`

**GET /api/journal-entries/{id}**

- Calls `JournalEntryService.GetJournalEntryByIdAsync`
- Returns:
- 200 OK with `JournalEntryResponse`
- 404 Not Found if entry doesn't exist
- Includes all lines in response

**Controller Design:**

- Thin controller (delegates to service)
- No business logic
- Exception handling via middleware
- Proper HTTP status codes

### 6. Dependency Injection

**File:** `src/Ledger.Api/Program.cs`Register:

- `IRequestHashService` → `RequestHashService` (scoped or singleton)
- `IJournalEntryRepository` → `JournalEntryRepository` (scoped)
- `IJournalEntryService` → `JournalEntryService` (scoped)

### 7. Unit Tests

**File:** `tests/Ledger.Tests/Application/Services/JournalEntryServiceTests.cs`Test scenarios:

- PostJournalEntryAsync: valid entry (success)
- PostJournalEntryAsync: less than 2 lines (INVALID_LINE_COUNT)
- PostJournalEntryAsync: amount <= 0 (INVALID_AMOUNT)
- PostJournalEntryAsync: amount scale > 4 (INVALID_AMOUNT_SCALE)
- PostJournalEntryAsync: invalid direction (INVALID_DIRECTION)
- PostJournalEntryAsync: account not found (ACCOUNT_NOT_FOUND)
- PostJournalEntryAsync: account inactive (ACCOUNT_INACTIVE)
- PostJournalEntryAsync: unbalanced entry (UNBALANCED_ENTRY)
- PostJournalEntryAsync: idempotent replay (same ExternalId + same hash)
- PostJournalEntryAsync: idempotency conflict (same ExternalId + different hash)
- PostJournalEntryAsync: exact decimal balance (no rounding issues)
- GetJournalEntryByIdAsync: found and not found

**File:** `tests/Ledger.Tests/Application/Services/RequestHashServiceTests.cs`Test scenarios:

- ComputeHash: deterministic (same input → same hash)
- ComputeHash: different input → different hash
- ComputeHash: canonical format (sorted lines)
- ComputeHash: handles null ExternalId correctly

### 8. Integration Tests

**File:** `tests/Ledger.Tests/Integration/Api/JournalEntriesControllerTests.cs`Test scenarios:

- POST: valid entry (201 Created)
- POST: less than 2 lines (400 Bad Request)
- POST: unbalanced entry (400 Bad Request)
- POST: account not found (400 Bad Request)
- POST: account inactive (400 Bad Request)
- POST: idempotent replay (200 OK, IdempotencyReplay = true)
- POST: idempotency conflict (409 Conflict)
- POST: exact decimal balance validation
- GET: existing entry (200 OK with lines)
- GET: not found (404 Not Found)
- Verify atomic transaction (rollback on error)
- Verify correlationId in responses

### 9. Concurrency Test

**File:** `tests/Ledger.Tests/Integration/Api/JournalEntriesConcurrencyTests.cs`Test scenario:

- Multiple threads/requests with same ExternalId + same hash
- Verify only one entry is created
- Verify all requests return 200 OK with same entry
- Verify no duplicate entries in database
- Use Task.WhenAll for parallel execution
- Verify database constraint prevents duplicates

**Implementation:**

```csharp
[Fact]
public async Task POST_JournalEntries_ConcurrentSameExternalId_CreatesOnlyOneEntry()
{
    // Arrange: Create accounts
    // Create 10 parallel requests with same ExternalId and same payload
    
    // Act: Execute all requests concurrently
    
    // Assert:
    // - All requests return 200 OK
    // - All responses have same JournalEntry.Id
    // - Database contains exactly 1 entry with this ExternalId
    // - All responses have IdempotencyReplay = true (except possibly first)
}
```



### 10. Decimal Precision Handling

**Critical:** Ensure exact decimal matching for balance validation:

- Use `decimal` type (never float/double)
- Compare debits and credits with `==` (exact match)
- No tolerance or rounding
- Validate scale ≤ 4 before comparison
- Use `Math.Round(amount, 4, MidpointRounding.ToEven)` for scale validation only

### 11. Request Hash Canonical Format

**Implementation Details:**

- Sort lines by: AccountId (ascending), then Direction (ascending), then Amount (ascending)
- Serialize to JSON with:
- No formatting (compact)
- Property names in consistent order
- Null ExternalId excluded from hash
- Use System.Text.Json with JsonSerializerOptions

**Example:**

```json
{"ExternalId":"ext-123","Lines":[{"AccountId":"...","Direction":1,"Amount":100.00},{"AccountId":"...","Direction":2,"Amount":100.00}]}
```



### 12. Database Transaction Handling

**Implementation:**

- Use `DbContext.Database.BeginTransactionAsync()`
- Wrap entry + lines creation in transaction
- Commit after successful SaveChangesAsync
- Rollback on any exception
- Ensure atomicity (all or nothing)

**Error Handling:**

- Catch DbUpdateException for unique constraint violations
- Map to ConflictException with appropriate reasonCode
- Ensure transaction rollback on all errors

### 13. Documentation Updates

**File:** `docs/VALIDATION_MATRIX.md`Update Journal Entry Posting section:

- Mark implemented validations as tested
- Add integration test coverage notes
- Document idempotency behavior
- Document concurrency safety

**File:** `PROMPTS.md`Add Phase 4 entry documenting:

- DTO design decisions
- Request hash computation strategy
- Service layer validation rules
- Idempotency implementation
- Atomic transaction handling
- Test coverage approach
- Concurrency test results

## File Structure

```javascript
src/Ledger.Api/
├── Controllers/
│   └── JournalEntriesController.cs (new)
└── DTOs/
    └── JournalEntries/
        ├── CreateJournalEntryRequest.cs (new)
        ├── CreateJournalEntryLineRequest.cs (new)
        ├── JournalEntryResponse.cs (new)
        └── JournalEntryLineResponse.cs (new)

src/Ledger.Application/
├── Repositories/
│   └── IJournalEntryRepository.cs (new)
├── Services/
    ├── IJournalEntryService.cs (new)
    ├── JournalEntryService.cs (new)
    ├── JournalEntryResult.cs (new)
    ├── IRequestHashService.cs (new)
    └── RequestHashService.cs (new)

src/Ledger.Infrastructure/
└── Repositories/
    └── JournalEntryRepository.cs (new)

tests/Ledger.Tests/
├── Application/
│   └── Services/
│       ├── JournalEntryServiceTests.cs (new)
│       └── RequestHashServiceTests.cs (new)
└── Integration/
    └── Api/
        ├── JournalEntriesControllerTests.cs (new)
        └── JournalEntriesConcurrencyTests.cs (new)
```



## Implementation Details

### Balance Validation Algorithm

```csharp
var totalDebits = lines
    .Where(l => l.Direction == LineDirection.Debit)
    .Sum(l => l.Amount);

var totalCredits = lines
    .Where(l => l.Direction == LineDirection.Credit)
    .Sum(l => l.Amount);

if (totalDebits != totalCredits)
{
    throw new ValidationException(
        $"Journal entry is unbalanced. Debits: {totalDebits}, Credits: {totalCredits}",
        "UNBALANCED_ENTRY");
}
```



### Idempotency Check Algorithm

```csharp
if (!string.IsNullOrWhiteSpace(request.ExternalId))
{
    var existing = await _repository.FindByExternalIdAsync(request.ExternalId, cancellationToken);
    if (existing != null)
    {
        var computedHash = _hashService.ComputeHash(request);
        if (existing.RequestHash == computedHash)
        {
            // Idempotent replay
            var lines = await _repository.GetLinesByJournalEntryIdAsync(existing.Id, cancellationToken);
            return new JournalEntryResult(existing, lines, IsIdempotencyReplay: true);
        }
        else
        {
            // Payload mismatch
            throw new ConflictException(
                "A journal entry with this external ID already exists with a different payload.",
                "DUPLICATE_EXTERNAL_ID");
        }
    }
}
```



### Atomic Transaction Pattern

```csharp
await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
try
{
    var entry = new JournalEntry(...);
    _context.JournalEntries.Add(entry);
    await _context.SaveChangesAsync(cancellationToken);
    
    foreach (var line in lines)
    {
        _context.JournalEntryLines.Add(line);
    }
    await _context.SaveChangesAsync(cancellationToken);
    
    await transaction.CommitAsync(cancellationToken);
    return (entry, lines);
}
catch
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```



## Verification Steps

1. All endpoints compile and run
2. Unit tests pass
3. Integration tests pass with Testcontainers
4. Concurrency test passes (no duplicates)
5. Balance validation works (exact decimal match)
6. Idempotency works (replay and conflict scenarios)
7. Atomic transaction works (rollback on error)
8. Request hash is deterministic
9. All error responses include reasonCode and correlationId
10. Documentation updated

## Critical Considerations

- **Decimal Precision:** Must use exact decimal comparison, no rounding tolerance
- **Concurrency Safety:** No pre-check patterns, rely on DB unique constraint
- **Atomicity:** Entry + lines must be in single transaction
- **Idempotency:** Same ExternalId + same hash = replay, different hash = conflict