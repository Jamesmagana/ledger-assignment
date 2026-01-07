---
name: EF Core Mappings and Database Constraints
overview: Implement EF Core entity configurations with PostgreSQL-specific constraints including money precision, CHECK constraints, unique indexes, and proper relationship configurations. Generate initial migration and document all database-level constraints.
todos:
  - id: create-account-config
    content: Create AccountConfiguration with case-insensitive unique index on Name, proper column types, and UTC timestamps
    status: completed
  - id: create-journal-entry-config
    content: Create JournalEntryConfiguration with unique partial index on ExternalId, RequestHash column, and UTC timestamps
    status: completed
  - id: create-journal-entry-line-config
    content: Create JournalEntryLineConfiguration with numeric(20,4) for Amount, CHECK constraint Amount > 0, and no cascade deletes
    status: completed
  - id: update-dbcontext
    content: Add DbSet properties and apply configurations in LedgerDbContext.OnModelCreating
    status: completed
    dependencies:
      - create-account-config
      - create-journal-entry-config
      - create-journal-entry-line-config
  - id: generate-migration
    content: Generate initial migration using EF Core tools and verify all constraints are included
    status: completed
    dependencies:
      - update-dbcontext
  - id: create-constraints-doc
    content: Create DATABASE_CONSTRAINTS.md documenting all database-level constraints and their purposes
    status: pending
    dependencies:
      - generate-migration
  - id: update-prompts
    content: Update PROMPTS.md Phase 2 with EF Core configuration and constraint decisions
    status: pending
    dependencies:
      - generate-migration
  - id: update-validation-matrix
    content: Update VALIDATION_MATRIX.md to reflect database-level enforcement for constraints
    status: completed
    dependencies:
      - generate-migration
---

# EF Core Mappings and Database Constraints Plan

## Overview

Implement EF Core entity configurations in the Infrastructure layer, establishing database constraints that backstop critical ledger invariants. This includes money precision, CHECK constraints, unique indexes, and proper relationship configurations.

## Implementation Tasks

### 1. Entity Configuration Classes

**File:** `src/Ledger.Infrastructure/Data/Configurations/AccountConfiguration.cs`Configure Account entity:

- Primary key: `Id` (Guid)
- `Name`: Required, max length (e.g., 200), case-insensitive uniqueness
- `Type`: Required, stored as integer (enum)
- `IsActive`: Required, default true
- `CreatedAt`: Required, UTC timestamp
- `UpdatedAt`: Required, UTC timestamp
- **Case-insensitive uniqueness**: Use normalized column approach (NameNormalized) or CITEXT
- Decision: Use normalized column (UPPER(Name)) for better control and portability
- Create unique index on normalized column

**File:** `src/Ledger.Infrastructure/Data/Configurations/JournalEntryConfiguration.cs`Configure JournalEntry entity:

- Primary key: `Id` (Guid)
- `ExternalId`: Optional (nullable), unique partial index (WHERE ExternalId IS NOT NULL)
- `RequestHash`: Required, max length (64 for SHA-256 hex string)
- `PostedAt`: Required, UTC timestamp
- `CreatedAt`: Required, UTC timestamp
- `UpdatedAt`: Required, UTC timestamp
- **Unique partial index**: `CREATE UNIQUE INDEX IX_JournalEntry_ExternalId ON JournalEntries (ExternalId) WHERE ExternalId IS NOT NULL`

**File:** `src/Ledger.Infrastructure/Data/Configurations/JournalEntryLineConfiguration.cs`Configure JournalEntryLine entity:

- Primary key: `Id` (Guid)
- `JournalEntryId`: Required, foreign key to JournalEntry (no cascade delete)
- `AccountId`: Required, foreign key to Account (no cascade delete)
- `Direction`: Required, stored as integer (enum)
- `Amount`: Required, **numeric(20,4)** precision
- **CHECK constraint**: `Amount > 0`
- **No cascade deletes**: Configure relationships with `DeleteBehavior.Restrict` or `DeleteBehavior.NoAction`

### 2. Update LedgerDbContext

**File:** `src/Ledger.Infrastructure/Data/LedgerDbContext.cs`

- Add `DbSet<Account> Accounts`
- Add `DbSet<JournalEntry> JournalEntries`
- Add `DbSet<JournalEntryLine> JournalEntryLines`
- Apply configurations in `OnModelCreating`:
- `modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerDbContext).Assembly)`
- Or explicitly apply each configuration

### 3. PostgreSQL-Specific Constraints

**Money Precision:**

- Configure `Amount` as `HasColumnType("numeric(20,4)")`
- Use `HasPrecision(20, 4)` for EF Core 8

**CHECK Constraints:**

- `Amount > 0` on JournalEntryLine
- Use `HasCheckConstraint("CK_JournalEntryLine_AmountPositive", "Amount > 0")`

**Case-Insensitive Uniqueness:**

- Option A: Use normalized column (NameNormalized) with unique index
- Add computed column or handle in application
- Create unique index on UPPER(Name)
- Option B: Use PostgreSQL CITEXT extension
- Requires extension: `CREATE EXTENSION IF NOT EXISTS citext;`
- Use `HasColumnType("citext")`
- **Decision**: Use normalized column approach for portability and explicit control

**Unique Partial Index:**

- `ExternalId` unique where not null
- Use `HasIndex(e => e.ExternalId).IsUnique().HasFilter("ExternalId IS NOT NULL")`

**UTC Timestamps:**

- Configure all DateTime properties as UTC
- Use `HasColumnType("timestamp with time zone")` or `HasColumnType("timestamptz")`
- Set default value: `HasDefaultValueSql("NOW()")` or `HasDefaultValueSql("CURRENT_TIMESTAMP")`

### 4. Relationship Configuration

**JournalEntryLine Relationships:**

- `JournalEntryId` → `JournalEntry.Id`
- Required
- No cascade delete: `DeleteBehavior.Restrict`
- `AccountId` → `Account.Id`
- Required
- No cascade delete: `DeleteBehavior.Restrict`

**Navigation Properties (Optional):**

- Add navigation properties in configurations if needed for queries
- Not required for basic CRUD operations

### 5. Migration Generation

**Command:** `dotnet ef migrations add InitialCreate --project src/Ledger.Infrastructure --startup-project src/Ledger.Api`**Migration File:** `src/Ledger.Infrastructure/Migrations/YYYYMMDDHHMMSS_InitialCreate.cs`Verify migration includes:

- All tables (Accounts, JournalEntries, JournalEntryLines)
- Primary keys
- Foreign keys
- Unique indexes (Account name normalized, JournalEntry ExternalId partial)
- CHECK constraint (Amount > 0)
- Money precision (numeric(20,4))
- UTC timestamps

### 6. Constraint Documentation

**File:** `docs/DATABASE_CONSTRAINTS.md` (new)Document all database constraints:

- Primary keys
- Foreign keys
- Unique constraints
- CHECK constraints
- Indexes
- Data types and precision
- Relationship behaviors (no cascade deletes)

### 7. Update Documentation

**File:** `PROMPTS.md`Update Phase 2 with:

- EF Core configuration approach
- Database constraint decisions
- Migration generation
- Constraint documentation

**File:** `docs/VALIDATION_MATRIX.md`Update validation matrix to reflect:

- Database-level enforcement for Amount > 0
- Database-level enforcement for Account name uniqueness
- Database-level enforcement for ExternalId uniqueness
- Money precision at database level

## Implementation Details

### Account Name Uniqueness Strategy

**Approach: Normalized Column**

1. Add computed column or handle normalization in application
2. Create unique index on normalized value
3. Alternative: Use PostgreSQL CITEXT (requires extension)

**Implementation:**

```csharp
// In AccountConfiguration
entity.Property(e => e.Name)
    .IsRequired()
    .HasMaxLength(200);

// Create unique index on normalized name
entity.HasIndex(e => e.Name)
    .HasDatabaseName("IX_Accounts_Name")
    .IsUnique()
    .HasFilter(null); // Will use UPPER() in raw SQL or computed column
```

**Better Approach:** Use raw SQL in migration for case-insensitive unique index:

```sql
CREATE UNIQUE INDEX IX_Accounts_Name_Normalized ON "Accounts" (UPPER("Name"));
```



### Money Precision

**EF Core Configuration:**

```csharp
entity.Property(e => e.Amount)
    .IsRequired()
    .HasPrecision(20, 4)
    .HasColumnType("numeric(20,4)");
```



### CHECK Constraint

**EF Core Configuration:**

```csharp
entity.HasCheckConstraint("CK_JournalEntryLine_AmountPositive", "Amount > 0");
```



### Unique Partial Index

**EF Core Configuration:**

```csharp
entity.HasIndex(e => e.ExternalId)
    .IsUnique()
    .HasFilter("\"ExternalId\" IS NOT NULL");
```



### UTC Timestamps

**EF Core Configuration:**

```csharp
entity.Property(e => e.CreatedAt)
    .IsRequired()
    .HasColumnType("timestamp with time zone")
    .HasDefaultValueSql("CURRENT_TIMESTAMP");

entity.Property(e => e.UpdatedAt)
    .IsRequired()
    .HasColumnType("timestamp with time zone")
    .HasDefaultValueSql("CURRENT_TIMESTAMP");
```



## File Structure

```javascript
src/Ledger.Infrastructure/
├── Data/
│   ├── LedgerDbContext.cs (modify)
│   └── Configurations/
│       ├── AccountConfiguration.cs (new)
│       ├── JournalEntryConfiguration.cs (new)
│       └── JournalEntryLineConfiguration.cs (new)
└── Migrations/
    └── YYYYMMDDHHMMSS_InitialCreate.cs (generated)

docs/
└── DATABASE_CONSTRAINTS.md (new)
```



## Verification Steps

1. All entity configurations compile
2. Migration generates successfully
3. Migration includes all required constraints:

- numeric(20,4) for Amount
- CHECK Amount > 0
- Unique index on Account name (case-insensitive)
- Unique partial index on JournalEntry.ExternalId
- No cascade deletes
- UTC timestamps

4. Migration applies to database successfully
5. Constraints are documented
6. PROMPTS.md and VALIDATION_MATRIX.md updated

## Dependencies