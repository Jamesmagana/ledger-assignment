---
name: Domain Entities Implementation
overview: Design and implement pure Domain entities (Account, JournalEntry, JournalEntryLine) with enums, following Clean Architecture principles. Entities will be free of EF attributes, with immutability expectations documented. This establishes the domain model foundation before EF Core mappings.
todos:
  - id: create-enums
    content: Create AccountType and LineDirection enums with explicit integer values
    status: completed
  - id: create-base-entity
    content: Create BaseEntity abstract class with Id, CreatedAt, UpdatedAt using init accessors
    status: completed
  - id: create-account-entity
    content: Create Account entity inheriting from BaseEntity with Name, Type, IsActive properties
    status: completed
    dependencies:
      - create-base-entity
      - create-enums
  - id: create-journal-entry-entity
    content: Create JournalEntry entity inheriting from BaseEntity with ExternalId, RequestHash, PostedAt properties
    status: completed
    dependencies:
      - create-base-entity
  - id: create-journal-entry-line-entity
    content: Create JournalEntryLine entity with Id, JournalEntryId, AccountId, Direction, Amount properties
    status: completed
    dependencies:
      - create-enums
  - id: add-xml-documentation
    content: Add XML documentation comments to all entities documenting immutability expectations
    status: completed
    dependencies:
      - create-account-entity
      - create-journal-entry-entity
      - create-journal-entry-line-entity
  - id: update-prompts
    content: Update PROMPTS.md Phase 2 with domain entity implementation decisions
    status: completed
    dependencies:
      - add-xml-documentation
---

# Domain Entities Implementation Plan

## Overview

Create pure Domain entities following Clean Architecture principles. Entities will be free of EF Core attributes and dependencies, establishing the core domain model before persistence layer implementation.

## Entity Design

### 1. Account Entity

**File:** `src/Ledger.Domain/Entities/Account.cs`**Properties:**

- `Id` (Guid) - Primary key
- `Name` (string) - Account name, must be unique case-insensitively
- `Type` (AccountType enum) - Account type (Asset, Liability, Equity, Revenue, Expense)
- `IsActive` (bool) - Account activation state
- `CreatedAt` (DateTime) - UTC timestamp
- `UpdatedAt` (DateTime) - UTC timestamp

**Design Decisions:**

- Immutable after creation (no setters for critical fields)
- Name uniqueness enforced at DB level (case-insensitive)
- Type cannot change after first usage (business rule in Application layer)
- IsActive can be toggled (soft delete pattern)

### 2. JournalEntry Entity

**File:** `src/Ledger.Domain/Entities/JournalEntry.cs`**Properties:**

- `Id` (Guid) - Primary key
- `ExternalId` (string?) - Nullable, for idempotency (unique at DB level)
- `RequestHash` (string) - SHA-256 hash of canonical request payload
- `PostedAt` (DateTime) - UTC timestamp when entry was posted
- `CreatedAt` (DateTime) - UTC timestamp
- `UpdatedAt` (DateTime) - UTC timestamp

**Design Decisions:**

- **IMMUTABLE after posting** - No modifications allowed after `PostedAt` is set
- ExternalId is nullable (not all entries may have external IDs)
- RequestHash used for idempotency validation
- PostedAt indicates when entry became immutable

### 3. JournalEntryLine Entity

**File:** `src/Ledger.Domain/Entities/JournalEntryLine.cs`**Properties:**

- `Id` (Guid) - Primary key
- `JournalEntryId` (Guid) - Foreign key to JournalEntry
- `AccountId` (Guid) - Foreign key to Account
- `Direction` (LineDirection enum) - Debit or Credit
- `Amount` (decimal) - Must be > 0, precision numeric(20,4)

**Design Decisions:**

- Amount must be positive (enforced at DB and Application layers)
- Direction is exclusive (Debit OR Credit, never both)
- Lines are immutable once created (part of immutable JournalEntry)
- No navigation properties in Domain (Infrastructure will add these)

### 4. Enums

**File:** `src/Ledger.Domain/Enums/AccountType.cs`

```csharp
public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Revenue = 4,
    Expense = 5
}
```

**File:** `src/Ledger.Domain/Enums/LineDirection.cs`

```csharp
public enum LineDirection
{
    Debit = 1,
    Credit = 2
}
```

**Design Decisions:**

- Explicit integer values for database storage
- Clear naming following accounting conventions

## Immutability Documentation

**File:** `src/Ledger.Domain/README.md` (or inline XML comments)Document immutability expectations:

- **JournalEntry**: Immutable after `PostedAt` is set. Corrections via reversing entries only.
- **JournalEntryLine**: Immutable once created (part of immutable JournalEntry).
- **Account**: Name and Type are immutable after creation. Only `IsActive` can change.

## Implementation Details

### Entity Structure

All entities will:

- Use `Guid` for primary keys (no auto-increment)
- Use `DateTime` for timestamps (UTC enforced in Application layer)
- Use `decimal` for money amounts (never float/double)
- Have no EF Core attributes (`[Key]`, `[Required]`, etc.)
- Have no navigation properties (Infrastructure layer responsibility)
- Include XML documentation comments

### Base Entity Pattern (Optional)

Consider a base class for common fields:

- `Id` (Guid)
- `CreatedAt` (DateTime)
- `UpdatedAt` (DateTime)

**Decision:** Use base class to reduce duplication and ensure consistency.**File:** `src/Ledger.Domain/Entities/BaseEntity.cs`

```csharp
public abstract class BaseEntity
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
```

**Note:** `init` accessors enforce immutability for creation-time fields.

### Constructor Patterns

- **Account**: Constructor with Name, Type, IsActive (Id and timestamps set by Infrastructure)
- **JournalEntry**: Constructor with ExternalId, RequestHash, PostedAt (Id and timestamps set by Infrastructure)
- **JournalEntryLine**: Constructor with JournalEntryId, AccountId, Direction, Amount (Id set by Infrastructure)

**Alternative:** Use factory methods or builders if needed for complex validation.

## File Structure

```javascript
src/Ledger.Domain/
├── Entities/
│   ├── BaseEntity.cs (new)
│   ├── Account.cs (new)
│   ├── JournalEntry.cs (new)
│   └── JournalEntryLine.cs (new)
└── Enums/
    ├── AccountType.cs (new)
    └── LineDirection.cs (new)
```



## Validation Notes

Domain entities will NOT contain validation logic (per Clean Architecture):

- Validation happens in Application layer
- Database constraints backstop critical invariants
- Domain entities represent the structure, not the rules

## Dependencies

- None (Domain layer has no dependencies)
- Uses only .NET base types (Guid, DateTime, string, decimal, enum)

## Next Steps (Not in This Phase)

- EF Core entity configurations in Infrastructure layer
- Database migrations
- Application layer validators
- Repository interfaces

## Documentation Updates

Update `PROMPTS.md` with Phase 2 decisions:

- Entity structure and properties