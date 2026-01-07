---
name: Trial Balance Reporting Implementation
overview: Implement Trial Balance reporting API with correct aggregation, zero-activity account inclusion, balance validation, and optional asOf date filtering. Include comprehensive integration tests to ensure correctness and no duplicates.
todos:
  - id: create-trial-balance-dtos
    content: "Create DTOs: TrialBalanceResponse, TrialBalanceItemResponse with proper structure"
    status: completed
  - id: create-trial-balance-models
    content: "Create Application models: TrialBalanceItem, TrialBalanceResult"
    status: completed
  - id: create-trial-balance-repository
    content: Create ITrialBalanceRepository interface and TrialBalanceRepository implementation with LEFT JOIN query for aggregation
    status: completed
  - id: create-trial-balance-service
    content: Create ITrialBalanceService and TrialBalanceService with balance validation (TotalNet == 0)
    status: completed
    dependencies:
      - create-trial-balance-repository
  - id: register-trial-balance-services
    content: Register TrialBalanceRepository and TrialBalanceService in Program.cs dependency injection
    status: completed
    dependencies:
      - create-trial-balance-service
  - id: create-reports-controller
    content: Create ReportsController with GET /api/reports/trial-balance endpoint supporting optional asOf query parameter
    status: completed
    dependencies:
      - create-trial-balance-service
  - id: create-integration-tests
    content: "Create integration tests for TrialBalanceController covering all scenarios: all accounts, zero-activity accounts, asOf filtering, balance validation, no duplicates"
    status: completed
    dependencies:
      - create-reports-controller
  - id: update-validation-matrix
    content: Update VALIDATION_MATRIX.md to reflect implemented and tested trial balance validations
    status: completed
    dependencies:
      - create-integration-tests
  - id: update-prompts
    content: Update PROMPTS.md with Phase 5 Trial Balance reporting implementation decisions
    status: completed
    dependencies:
      - create-integration-tests
---

# Trial Balance Reporting Implementation Plan

## Overview

Implement the Trial Balance reporting API that aggregates journal entry lines by account, calculates net balances (debits - credits), includes all accounts (even with zero activity), and validates that total net equals zero. Support optional `asOf` date filtering.

## Current State

- Domain entities: `Account`, `JournalEntry`, `JournalEntryLine` exist
- Database schema: Accounts, JournalEntries, JournalEntryLines tables with proper relationships
- EF Core DbContext available with all entities
- Repository pattern established for Accounts and JournalEntries
- No reporting service or repository yet
- No trial balance DTOs or endpoints yet

## Implementation Tasks

### 1. DTOs (Data Transfer Objects)

**File:** `src/Ledger.Api/DTOs/Reports/TrialBalanceItemResponse.cs`

```csharp
public record TrialBalanceItemResponse(
    Guid AccountId,
    string AccountName,
    AccountType AccountType,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Net
);
```

**File:** `src/Ledger.Api/DTOs/Reports/TrialBalanceResponse.cs`

```csharp
public record TrialBalanceResponse(
    DateTime? AsOf,
    IReadOnlyList<TrialBalanceItemResponse> Items,
    decimal TotalNet
);
```

**Design Decisions:**

- Each account appears exactly once
- Zero-activity accounts have TotalDebits=0, TotalCredits=0, Net=0
- TotalNet is the sum of all Net values (must equal 0)
- AsOf is nullable (null means all entries up to now)

### 2. Repository Pattern

**File:** `src/Ledger.Application/Repositories/ITrialBalanceRepository.cs`Interface method:

- `Task<IReadOnlyList<TrialBalanceItem>> GetTrialBalanceAsync(DateTime? asOf, CancellationToken cancellationToken = default)`

**File:** `src/Ledger.Application/Models/TrialBalanceItem.cs`

```csharp
public record TrialBalanceItem(
    Guid AccountId,
    string AccountName,
    AccountType AccountType,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Net
);
```

**File:** `src/Ledger.Infrastructure/Repositories/TrialBalanceRepository.cs`Implementation using EF Core:

- LEFT JOIN Accounts with JournalEntryLines
- Filter by `JournalEntry.PostedAt <= asOf` (if asOf provided)
- Group by Account
- Aggregate using CASE expressions:
- `SUM(CASE WHEN Direction = Debit THEN Amount ELSE 0 END)` as TotalDebits
- `SUM(CASE WHEN Direction = Credit THEN Amount ELSE 0 END)` as TotalCredits
- Calculate Net = TotalDebits - TotalCredits
- Order by AccountName for consistent output
- Use raw SQL or LINQ with proper grouping

**Query Strategy:**

```sql
SELECT 
    a.Id AS AccountId,
    a.Name AS AccountName,
    a.Type AS AccountType,
    COALESCE(SUM(CASE WHEN l.Direction = 1 THEN l.Amount ELSE 0 END), 0) AS TotalDebits,
    COALESCE(SUM(CASE WHEN l.Direction = 2 THEN l.Amount ELSE 0 END), 0) AS TotalCredits,
    COALESCE(SUM(CASE WHEN l.Direction = 1 THEN l.Amount ELSE 0 END), 0) - 
    COALESCE(SUM(CASE WHEN l.Direction = 2 THEN l.Amount ELSE 0 END), 0) AS Net
FROM "Accounts" a
LEFT JOIN "JournalEntryLines" l ON a.Id = l."AccountId"
LEFT JOIN "JournalEntries" je ON l."JournalEntryId" = je.Id
WHERE (je."PostedAt" <= @asOf OR @asOf IS NULL OR je."PostedAt" IS NULL)
GROUP BY a.Id, a.Name, a.Type
ORDER BY a.Name
```

**Alternative (LINQ approach):**

- Use EF Core LINQ with GroupJoin or GroupBy
- Handle nulls properly (zero-activity accounts)
- Ensure all accounts are included

### 3. Service Layer

**File:** `src/Ledger.Application/Services/ITrialBalanceService.cs`Interface method:

- `Task<TrialBalanceResult> GetTrialBalanceAsync(DateTime? asOf, CancellationToken cancellationToken = default)`

**File:** `src/Ledger.Application/Services/TrialBalanceResult.cs`

```csharp
public record TrialBalanceResult(
    DateTime? AsOf,
    IReadOnlyList<TrialBalanceItem> Items,
    decimal TotalNet
);
```

**File:** `src/Ledger.Application/Services/TrialBalanceService.cs`Implementation:

- Call repository to get trial balance items
- Calculate TotalNet = sum of all Net values
- Validate TotalNet == 0 (throw ValidationException if not)
- Return TrialBalanceResult

**Validation:**

- If TotalNet != 0, throw ValidationException with reasonCode "UNBALANCED_TRIAL_BALANCE"
- This ensures data integrity (double-entry accounting must balance)

### 4. Controller Implementation

**File:** `src/Ledger.Api/Controllers/ReportsController.cs`**GET /api/reports/trial-balance?asOf=optional**

- Accepts optional `asOf` query parameter (DateTime)
- Calls `TrialBalanceService.GetTrialBalanceAsync`
- Maps `TrialBalanceResult` to `TrialBalanceResponse`
- Returns 200 OK with trial balance data
- Returns 400 Bad Request if trial balance is unbalanced (validation error)

**Controller Design:**

- Thin controller (delegates to service)
- Query parameter binding for `asOf`
- Proper HTTP status codes
- Exception handling via middleware

### 5. Dependency Injection

**File:** `src/Ledger.Api/Program.cs`Register:

- `ITrialBalanceRepository` → `TrialBalanceRepository` (scoped)
- `ITrialBalanceService` → `TrialBalanceService` (scoped)

### 6. Integration Tests

**File:** `tests/Ledger.Tests/Integration/Api/TrialBalanceControllerTests.cs`Test scenarios:

- GET /api/reports/trial-balance: returns all accounts with correct balances
- GET /api/reports/trial-balance?asOf=date: filters by PostedAt <= asOf
- Verify each account appears exactly once
- Verify zero-activity accounts included with net=0
- Verify TotalNet equals 0
- Verify correct aggregation (debits - credits)
- Verify asOf filtering works correctly
- Verify accounts ordered by name
- Test with multiple journal entries
- Test with accounts that have no journal entries

**Test Data Setup:**

- Create multiple accounts (some with activity, some without)
- Create multiple journal entries with different PostedAt dates
- Verify aggregation correctness
- Verify no duplicate accounts in response

### 7. Edge Cases and Validation

**Edge Cases:**

- No accounts exist → empty list, TotalNet = 0
- No journal entries → all accounts with net=0, TotalNet = 0
- All accounts have zero activity → all nets = 0, TotalNet = 0
- asOf before any entries → all accounts with net=0
- asOf after all entries → includes all entries
- Multiple entries for same account → correct aggregation

**Validation:**

- TotalNet must equal 0 (enforced in service)
- Each account appears exactly once (enforced by GROUP BY)
- Zero-activity accounts included (enforced by LEFT JOIN)

### 8. Query Performance Considerations

**Optimization:**

- Use database indexes on JournalEntryLines (AccountId, JournalEntryId)
- Use database index on JournalEntries (PostedAt) for asOf filtering
- Consider materialized view for large datasets (future optimization)
- Use AsNoTracking() for read-only queries

### 9. Documentation Updates

**File:** `docs/VALIDATION_MATRIX.md`Update Trial Balance Report section:

- Mark implemented validations as tested
- Add integration test coverage notes
- Document query strategy
- Document balance validation

**File:** `PROMPTS.md`Add Phase 5 entry documenting:

- DTO design decisions
- Query strategy (LEFT JOIN, aggregation)
- Service layer validation (TotalNet == 0)
- Test coverage approach
- Edge case handling

## File Structure

```javascript
src/Ledger.Api/
├── Controllers/
│   └── ReportsController.cs (new)
└── DTOs/
    └── Reports/
        ├── TrialBalanceResponse.cs (new)
        └── TrialBalanceItemResponse.cs (new)

src/Ledger.Application/
├── Models/
│   └── TrialBalanceItem.cs (new)
├── Repositories/
│   └── ITrialBalanceRepository.cs (new)
└── Services/
    ├── ITrialBalanceService.cs (new)
    ├── TrialBalanceService.cs (new)
    └── TrialBalanceResult.cs (new)

src/Ledger.Infrastructure/
└── Repositories/
    └── TrialBalanceRepository.cs (new)

tests/Ledger.Tests/
└── Integration/
    └── Api/
        └── TrialBalanceControllerTests.cs (new)
```



## Implementation Details

### Query Implementation (LINQ Approach)

```csharp
var query = from account in _context.Accounts
            join line in _context.JournalEntryLines on account.Id equals line.AccountId into lines
            from line in lines.DefaultIfEmpty()
            join entry in _context.JournalEntries on line.JournalEntryId equals entry.Id into entries
            from entry in entries.DefaultIfEmpty()
            where asOf == null || entry == null || entry.PostedAt <= asOf
            group new { line, account } by new { account.Id, account.Name, account.Type } into g
            select new TrialBalanceItem(
                g.Key.Id,
                g.Key.Name,
                g.Key.Type,
                g.Sum(x => x.line != null && x.line.Direction == LineDirection.Debit ? x.line.Amount : 0),
                g.Sum(x => x.line != null && x.line.Direction == LineDirection.Credit ? x.line.Amount : 0),
                g.Sum(x => x.line != null && x.line.Direction == LineDirection.Debit ? x.line.Amount : 0) -
                g.Sum(x => x.line != null && x.line.Direction == LineDirection.Credit ? x.line.Amount : 0)
            );
```

**Note:** This LINQ approach may need refinement for proper LEFT JOIN behavior. Consider raw SQL for clarity and performance.

### Raw SQL Approach (Recommended)

```csharp
var sql = @"
    SELECT 
        a.""Id"" AS AccountId,
        a.""Name"" AS AccountName,
        a.""Type"" AS AccountType,
        COALESCE(SUM(CASE WHEN l.""Direction"" = 1 THEN l.""Amount"" ELSE 0 END), 0) AS TotalDebits,
        COALESCE(SUM(CASE WHEN l.""Direction"" = 2 THEN l.""Amount"" ELSE 0 END), 0) AS TotalCredits,
        COALESCE(SUM(CASE WHEN l.""Direction"" = 1 THEN l.""Amount"" ELSE 0 END), 0) - 
        COALESCE(SUM(CASE WHEN l.""Direction"" = 2 THEN l.""Amount"" ELSE 0 END), 0) AS Net
    FROM ""Accounts"" a
    LEFT JOIN ""JournalEntryLines"" l ON a.""Id"" = l.""AccountId""
    LEFT JOIN ""JournalEntries"" je ON l.""JournalEntryId"" = je.""Id""
    WHERE (@asOf IS NULL OR je.""PostedAt"" IS NULL OR je.""PostedAt"" <= @asOf)
    GROUP BY a.""Id"", a.""Name"", a.""Type""
    ORDER BY a.""Name""";

var items = await _context.Database
    .SqlQueryRaw<TrialBalanceItem>(sql, new NpgsqlParameter("asOf", asOf ?? (object)DBNull.Value))
    .ToListAsync(cancellationToken);
```

**Note:** `SqlQueryRaw` requires EF Core 8.0+ and proper mapping. Alternative: use `FromSqlRaw` with a view or use LINQ with proper joins.

### Balance Validation

```csharp
var totalNet = items.Sum(item => item.Net);
if (totalNet != 0)
{
    throw new ValidationException(
        $"Trial balance is unbalanced. Total net: {totalNet}",
        "UNBALANCED_TRIAL_BALANCE");
}
```



## Verification Steps

1. All endpoints compile and run
2. Integration tests pass with Testcontainers
3. Each account appears exactly once
4. Zero-activity accounts included with net=0
5. TotalNet equals 0 (validated)
6. Aggregation correct (debits - credits)
7. asOf filtering works correctly
8. Accounts ordered by name
9. All error responses include reasonCode and correlationId
10. Documentation updated

## Critical Considerations

- **LEFT JOIN:** Must use LEFT JOIN to include zero-activity accounts
- **Aggregation:** Use COALESCE to handle NULL values (zero-activity accounts)
- **Balance Validation:** TotalNet must equal 0 (enforced in service)
- **No Duplicates:** GROUP BY ensures each account appears once
- **asOf Filtering:** Filter by PostedAt <= asOf (include entries up to and including asOf)