# Ledger API Postman Collection

This Postman collection provides comprehensive test data and requests for the Ledger Financial Backbone System API.

## Setup Instructions

### 1. Import Collection and Environment

1. Open Postman
2. Click **Import** button
3. Import both files:
   - `Ledger_API.postman_collection.json` (Collection)
   - `Ledger_API.postman_environment.json` (Environment)
4. Select the **Ledger API - Local** environment from the environment dropdown

### 2. Update Base URL (if needed)

If your API is running on a different port, update the `baseUrl` variable in the environment:
- Default: `http://localhost:5169`
- Alternative (HTTPS): `https://localhost:7051`

### 3. Run the Collection

**Recommended execution order:**

1. **Authentication**
   - First: `Register User` - Creates a test user
   - Then: `Login` - Gets JWT token (automatically saved to `authToken` variable)

2. **Accounts** (Create accounts first - they're needed for journal entries)
   - `Create Account - Cash` (Asset)
   - `Create Account - Accounts Receivable` (Asset)
   - `Create Account - Accounts Payable` (Liability)
   - `Create Account - Sales Revenue` (Revenue)
   - `Create Account - Rent Expense` (Expense)
   - `Get All Accounts` - Verify all accounts were created
   - `Get Account By ID` - Get specific account
   - `Update Account (Deactivate)` - Test account update

3. **Journal Entries**
   - `Post Journal Entry - Sale Transaction` - AR Debit, Sales Credit
   - `Post Journal Entry - Cash Sale` - Cash Debit, Sales Credit
   - `Post Journal Entry - Rent Payment` - Rent Expense Debit, Cash Credit
   - `Post Journal Entry - Multi-Line (Complex)` - Multiple debits/credits
   - `Post Journal Entry - Idempotent` - Test idempotency (should return 200 OK)
   - `Get Journal Entry By ID` - Retrieve specific entry

4. **Reports**
   - `Get Trial Balance` - View all account balances (should total to zero)
   - `Get Trial Balance (As Of Date)` - Filter by date

## Test Data

### Account Types
- `1` = Asset
- `2` = Liability
- `3` = Equity
- `4` = Revenue
- `5` = Expense

### Line Directions
- `1` = Debit
- `2` = Credit

### Sample Test Scenarios

#### 1. Sale on Credit
- Debit: Accounts Receivable ($1,000)
- Credit: Sales Revenue ($1,000)

#### 2. Cash Sale
- Debit: Cash ($500)
- Credit: Sales Revenue ($500)

#### 3. Rent Payment
- Debit: Rent Expense ($1,200)
- Credit: Cash ($1,200)

#### 4. Complex Multi-Line Entry
- Debit: Cash ($2,000)
- Debit: Accounts Receivable ($1,500)
- Credit: Sales Revenue ($3,500)
- **Total Debits = Total Credits = $3,500**

## Automatic Variable Management

The collection automatically saves values to environment variables:

- **Authentication**: `authToken`, `userId`, `userEmail`
- **Accounts**: `cashAccountId`, `arAccountId`, `apAccountId`, `salesAccountId`, `rentAccountId`
- **Journal Entries**: `journalEntryId`

These variables are used in subsequent requests, so you don't need to manually copy IDs.

## Important Notes

1. **All endpoints (except Register/Login) require authentication**
   - The collection uses Bearer token authentication
   - Token is automatically set after login

2. **Double-Entry Validation**
   - All journal entries must balance (Total Debits = Total Credits)
   - The API will reject unbalanced entries with a 400 Bad Request

3. **Idempotency**
   - Journal entries support idempotency via `externalId`
   - Same `externalId` + same payload = 200 OK (replay)
   - Same `externalId` + different payload = 409 Conflict

4. **Account Names Must Be Unique**
   - Case-insensitive uniqueness enforced
   - Duplicate names return 409 Conflict

5. **Trial Balance**
   - Should always total to zero (all debits = all credits)
   - Includes zero-activity accounts

## Troubleshooting

### "Unauthorized" Errors
- Make sure you've run the `Login` request first
- Check that `authToken` variable is set in the environment

### "Account Not Found" Errors
- Ensure you've created the required accounts before posting journal entries
- Check that account IDs are correctly saved in environment variables

### "Unbalanced Entry" Errors
- Verify that total debits equal total credits
- Check that amounts are positive (> 0)
- Ensure at least 2 lines per entry

### Connection Errors
- Verify the API is running on the configured port
- Check `baseUrl` in the environment matches your API URL
- Ensure PostgreSQL database is running and migrations are applied

## Collection Structure

```
Ledger API
├── Authentication
│   ├── Register User
│   └── Login
├── Accounts
│   ├── Create Account - Cash
│   ├── Create Account - Accounts Receivable
│   ├── Create Account - Accounts Payable
│   ├── Create Account - Sales Revenue
│   ├── Create Account - Rent Expense
│   ├── Get All Accounts
│   ├── Get Account By ID
│   └── Update Account (Deactivate)
├── Journal Entries
│   ├── Post Journal Entry - Sale Transaction
│   ├── Post Journal Entry - Cash Sale
│   ├── Post Journal Entry - Rent Payment
│   ├── Post Journal Entry - Multi-Line (Complex)
│   ├── Post Journal Entry - Idempotent
│   └── Get Journal Entry By ID
└── Reports
    ├── Get Trial Balance
    └── Get Trial Balance (As Of Date)
```

## Running the Full Test Flow

You can use Postman's **Collection Runner** to execute all requests in sequence:

1. Click on the collection
2. Click **Run**
3. Select the requests you want to run (or run all)
4. Click **Run Ledger API**
5. Review the test results

The collection includes automatic variable extraction, so IDs from created resources are automatically used in subsequent requests.

