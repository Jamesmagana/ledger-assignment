---
name: Governance Document Alignment
overview: Review and align the three governance documents (.cursorrules, PROMPTS.md, VALIDATION_MATRIX.md) to ensure consistency, completeness, and readiness for code generation. The documents exist but need cross-validation to ensure all requirements are properly tracked.
todos:
  - id: update-validation-matrix-jwt
    content: Add JWT Authentication section to VALIDATION_MATRIX.md with validation rules for missing token, invalid token, expired token, wrong issuer/audience, and valid token scenarios
    status: pending
  - id: update-validation-matrix-users
    content: Add User Management section to VALIDATION_MATRIX.md covering email uniqueness, password validation, duplicate prevention, hashing, timing-safe comparison, enable/disable state, and login rules
    status: pending
  - id: update-validation-matrix-audit
    content: Add Audit Logging section to VALIDATION_MATRIX.md covering audit entry creation, old/new values capture, sensitive field exclusion, transaction integration, and immutability
    status: pending
  - id: verify-prompts-completeness
    content: Review PROMPTS.md to ensure all .cursorrules requirements are reflected in decision logs, verify phase descriptions align with rules, and add any missing architectural decisions
    status: pending
  - id: cross-reference-consistency
    content: Perform final cross-reference check between all three documents to verify alignment, identify any contradictions, and ensure all critical invariants are tracked
    status: pending
    dependencies:
      - update-validation-matrix-jwt
      - update-validation-matrix-users
      - update-validation-matrix-audit
      - verify-prompts-completeness
---

# Governance Document Alignment Plan

## Current State Analysis

Three governance documents exist:

- **[.cursorrules](.cursorrules)** - Comprehensive rules covering architecture, ledger invariants, idempotency, security, audit, and testing
- **[PROMPTS.md](PROMPTS.md)** - AI interaction log with 9 phases of development decisions
- **[docs/VALIDATION_MATRIX.md](docs/VALIDATION_MATRIX.md)** - Validation coverage matrix for accounts, journal entries, idempotency, and trial balance

## Alignment Tasks

### 1. Update VALIDATION_MATRIX.md

The validation matrix is missing coverage for features documented in `.cursorrules` and `PROMPTS.md`:**Add JWT Authentication Section:**

- Missing token → 401 Unauthorized
- Invalid token → 401 Unauthorized  
- Expired token → 401 Unauthorized
- Wrong issuer/audience → 401 Unauthorized
- Valid token → access granted

**Add User Management Section:**

- Email uniqueness (case-insensitive) → DB + App enforcement
- Password strength validation → API validation
- Duplicate user creation → 409 Conflict
- Password hashing → Infrastructure (bcrypt/PBKDF2)
- Timing-safe password comparison → Infrastructure
- User enable/disable state → App validation
- Invalid login (generic error, no enumeration) → App logic
- Failed login attempt tracking → Infrastructure
- Last login timestamp update → Infrastructure

**Add Audit Logging Section:**

- Audit entry created on write operations → Infrastructure
- Old/New values captured as JSON → Infrastructure
- Sensitive fields excluded (passwords, hashes) → Infrastructure
- Audit written in same transaction → Infrastructure
- Audit failure causes transaction failure → Infrastructure
- Audit records immutable (no updates/deletes) → DB + API

### 2. Verify PROMPTS.md Completeness

Ensure `PROMPTS.md` reflects all rules in `.cursorrules`:

- ✅ Phase 0: Governance & Guardrails
- ✅ Phase 1: Infrastructure & Scaffolding
- ✅ Phase 2: Domain & Persistence
- ✅ Phase 3: Accounts API
- ✅ Phase 4: Journal Entry Posting (idempotency)
- ✅ Phase 5: Financial Reporting
- ✅ Phase 6: Testing & Hardening
- ✅ Phase 7: JWT Authentication
- ✅ Phase 8: User Management & Login
- ✅ Phase 9: Audit Logging

**Action:** Verify all `.cursorrules` sections are represented in `PROMPTS.md` decisions. Add any missing architectural decisions if needed.

### 3. Cross-Reference Consistency Check

Verify alignment between documents:**Architecture Rules:**

- `.cursorrules` → Clean Architecture with 4 layers
- `PROMPTS.md` Phase 1 → Matches (5 projects including Tests)
- `VALIDATION_MATRIX.md` → No architecture section (acceptable, it's a validation matrix)

**Ledger Invariants:**

- `.cursorrules` → Double-entry, immutability, atomic writes
- `PROMPTS.md` Phase 2, 4 → Matches
- `VALIDATION_MATRIX.md` → Covered in Journal Entry Posting section

**Idempotency:**

- `.cursorrules` → externalId + request_hash pattern
- `PROMPTS.md` Phase 4 → Matches
- `VALIDATION_MATRIX.md` → Covered in Idempotency & Duplicates section

**Security:**

- `.cursorrules` → JWT, user management, password hashing
- `PROMPTS.md` Phase 7, 8 → Matches
- `VALIDATION_MATRIX.md` → **MISSING** (needs addition)

**Audit:**

- `.cursorrules` → Mandatory audit logging with specific fields
- `PROMPTS.md` Phase 9 → Matches
- `VALIDATION_MATRIX.md` → **MISSING** (needs addition)

### 4. Document Structure Verification

Ensure all documents follow consistent formatting:

- Headers use consistent markdown levels
- Tables in `VALIDATION_MATRIX.md` are properly formatted
- Cross-references are clear and maintainable

## Implementation Steps

1. **Update VALIDATION_MATRIX.md:**

- Add "JWT AUTHENTICATION" section with validation rules
- Add "USER MANAGEMENT" section with validation rules
- Add "AUDIT LOGGING" section with validation rules
- Maintain consistent table format (Rule | Layer | Enforcement | Error Code | Test)

2. **Review PROMPTS.md:**

- Verify all `.cursorrules` requirements are reflected in decision logs
- Ensure phase descriptions align with actual rules