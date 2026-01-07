# Ledger Validation Matrix
End-to-End Validation Coverage

==================================================
ACCOUNTS API
==================================================

| Rule                                | Layer     | Enforcement                  | Error Code               | Test         |
|-------------------------------------|-----------|------------------------------|--------------------------|--------------|
| Name required                       | API       | DTO validation               | INVALID_NAME             | Unit         |
| Name length limits                  | API       | DTO validation               | INVALID_NAME             | Unit         |
| Name uniqueness (case-insensitive)  | DB + App  | Unique index + check         | DUPLICATE_ACCOUNT_NAME   | Integration  |
| Valid AccountType enum              | API       | Enum validation              | INVALID_ACCOUNT_TYPE     | Unit         |
| Prevent type change after usage     | App       | Business rule                | ACCOUNT_TYPE_IMMUTABLE   | Integration  |
| Account exists                      | App       | Lookup                       | ACCOUNT_NOT_FOUND        | Unit         |
| Account active                      | App       | Flag check                   | ACCOUNT_INACTIVE         | Unit         |

==================================================
JOURNAL ENTRY POSTING
==================================================

| Rule                     | Layer     | Enforcement                  | Error Code             | Test                |
|--------------------------|-----------|------------------------------|------------------------|---------------------|
| Minimum 2 lines          | App       | Validation                   | INVALID_LINE_COUNT     | Unit                |
| Amount > 0               | API + DB  | Validator + CHECK            | INVALID_AMOUNT         | Integration         |
| Decimal scale ≤ 4         | API       | Validation                   | INVALID_AMOUNT_SCALE   | Unit                |
| Debit OR Credit only     | API       | Validation                   | INVALID_DIRECTION      | Unit                |
| Accounts exist           | App       | Lookup                       | ACCOUNT_NOT_FOUND      | Integration         |
| Account active           | App       | Lookup                       | ACCOUNT_INACTIVE       | Integration         |
| Debits == Credits        | App       | Pre-commit validation        | UNBALANCED_ENTRY       | Unit + Integration  |
| Atomic write             | DB        | Transaction                  | N/A                    | Integration         |

==================================================
IDEMPOTENCY & DUPLICATES
==================================================

| Scenario                           | Enforcement | Result           | Test        |
|------------------------------------|-------------|------------------|-------------|
| Same externalId, same payload      | DB + App    | 200 replay       | Integration |
| Same externalId, different payload | App         | 409 conflict     | Integration |
| Concurrent same externalId         | DB          | Single insert    | Integration |
| Duplicate prevention               | DB          | Unique constraint| Integration |

==================================================
TRIAL BALANCE REPORT
==================================================

| Rule                              | Layer    | Enforcement   | Test        |
|-----------------------------------|----------|---------------|-------------|
| Each account appears once         | DB query | Grouping      | Integration |
| Zero-activity accounts included   | DB query | LEFT JOIN     | Integration |
| Net = debits - credits            | DB query | Aggregate     | Integration |
| Total net equals 0                | App      | Validation    | Integration |
| asOf filter correctness           | DB query | WHERE clause  | Integration |

==================================================
JWT AUTHENTICATION (SIMPLE)
==================================================

| Rule                              | Layer | Enforcement            | Error Code     | Test         |
|-----------------------------------|-------|------------------------|----------------|--------------|
| JWT required for all endpoints    | API   | Auth middleware        | UNAUTHORIZED   | Integration  |
| Missing token rejected            | API   | JWT handler            | UNAUTHORIZED   | Integration  |
| Invalid signature rejected        | API   | JWT validation         | UNAUTHORIZED   | Unit         |
| Expired token rejected            | API   | JWT validation         | UNAUTHORIZED   | Unit         |
| Invalid issuer/audience rejected  | API   | JWT validation         | UNAUTHORIZED   | Unit         |
| CorrelationId on auth failures    | API   | Middleware             | N/A            | Unit         |

==================================================
USER CREATION
==================================================

| Rule                              | Layer     | Enforcement              | Error Code         | Test         |
|-----------------------------------|-----------|--------------------------|--------------------|--------------|
| Email required                    | API       | DTO validation           | INVALID_EMAIL      | Unit         |
| Valid email format                | API       | Validation               | INVALID_EMAIL      | Unit         |
| Email uniqueness (case-insensitive)| DB + App | Unique index             | DUPLICATE_USER     | Integration  |
| Password required                 | API       | DTO validation           | INVALID_PASSWORD   | Unit         |
| Password strength enforced        | App       | Business rule            | WEAK_PASSWORD      | Unit         |
| Password hashed                   | App       | Hashing service          | N/A                | Unit         |
| No password returned              | API       | Response contract        | N/A                | Unit         |
| User enabled by default           | App       | Default state            | N/A                | Unit         |

==================================================
USER LOGIN
==================================================

| Rule                              | Layer     | Enforcement              | Error Code           | Test         |
|-----------------------------------|-----------|--------------------------|----------------------|--------------|
| Email required                    | API       | DTO validation           | INVALID_CREDENTIALS  | Unit         |
| Password required                 | API       | DTO validation           | INVALID_CREDENTIALS  | Unit         |
| Invalid credentials generic error | App       | Auth logic               | INVALID_CREDENTIALS  | Integration  |
| No user enumeration               | API       | Error handling           | INVALID_CREDENTIALS  | Integration  |
| Disabled user blocked             | App       | State check              | USER_DISABLED        | Integration  |
| Failed login count tracked        | App       | State update             | N/A                  | Integration  |
| Successful login updates timestamp| App       | State update             | N/A                  | Integration  |
| JWT issued on success             | App       | Token generation         | N/A                  | Integration  |
| Login audited                     | Infra     | Audit interceptor        | N/A                  | Integration  |

==================================================
AUDIT LOGGING
==================================================

| Rule                              | Layer | Enforcement                  | Test         |
|-----------------------------------|-------|------------------------------|--------------|
| Audit on entity CREATE            | Infra | EF Core interceptor          | Integration  |
| Audit on entity UPDATE            | Infra | EF Core interceptor          | Integration  |
| Audit on journal POST             | Infra | EF Core interceptor          | Integration  |
| Audit on user creation            | Infra | EF Core interceptor          | Integration  |
| Audit on login                    | Infra | EF Core interceptor          | Integration  |
| Old values captured               | Infra | Change tracking              | Unit         |
| New values captured               | Infra | Change tracking              | Unit         |
| Sensitive fields excluded         | Infra | Field filter                 | Unit         |
| PerformedBy user captured         | API   | JWT context                  | Integration  |
| CorrelationId captured            | API   | Middleware                   | Integration  |
| Append-only behavior              | DB    | No update/delete paths       | Integration  |
| Audit failure blocks transaction  | DB    | Transaction rollback         | Integration  |

==================================================
GLOBAL RULES
==================================================

| Rule                       | Layer | Enforcement   | Test |
|----------------------------|-------|---------------|------|
| RFC7807 ProblemDetails     | API   | Middleware    | Unit |
| CorrelationId included     | API   | Middleware    | Unit |
| UTC timestamps             | App   | Guard         | Unit |
| No deletes for ledger      | API   | No endpoints  | N/A  |

==================================================
END OF MATRIX
==================================================
