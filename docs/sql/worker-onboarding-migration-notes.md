# AddWorkerOnboardingSlice — Deployment Notes

## Overview

This migration creates three new tables (WorkerInvites, WorkerProfiles, ComplianceDocuments).
The WorkerProfiles.NationalInsuranceNumber column is encrypted at the SQL Server column level
using Always Encrypted (deterministic encryption) per Plan 09 OQ-02.

## Prerequisites

Always Encrypted requires a Column Master Key (CMK) and Column Encryption Key (CEK) to exist
in the target database before the migration runs. If they are absent, the migration fails at
its first statement with a descriptive error (THROW 51001 or 51002).

## Production deployment sequence

1. Ensure the App Service managed identity has Azure Key Vault permissions:
   - Key Vault → Access Policies → Identity → Keys: Get, Unwrap Key, Wrap Key

2. Run `docs/sql/encryption-setup.sql` against the production database.
   - Substitute the actual Azure Key Vault key URL in the CMK creation block.
   - Generate the CEK encrypted value (SSMS wizard or scripts/generate-cek.ps1)
     and uncomment the CREATE COLUMN ENCRYPTION KEY statement.
   - Verify: `SELECT name FROM sys.column_master_keys; SELECT name FROM sys.column_encryption_keys;`

3. Ensure the App Service connection string includes `Column Encryption Setting=enabled`.

4. Apply the migration:
   ```
   dotnet ef database update \
     --project src/ElectCrm.Infrastructure \
     --startup-project src/ElectCrm.Presentation
   ```

5. Verify the NI column is encrypted:
   ```sql
   SELECT name, encryption_type_desc
   FROM sys.columns
   WHERE object_id = OBJECT_ID('WorkerProfiles')
     AND name = 'NationalInsuranceNumber';
   -- Expected: encryption_type_desc = 'DETERMINISTIC'
   ```

## Development setup (macOS)

1. `az login` — authenticate to Azure.

2. Confirm access to the shared dev Key Vault:
   ```
   az keyvault key show --vault-name <elect-dev-vault> --name <key-name>
   ```

3. Run `docs/sql/encryption-setup.sql` against your local SQL Server with the dev vault URL.
   Generate the CEK value first (SSMS or scripts/generate-cek.ps1) if not already present.

4. Add `Column Encryption Setting=enabled` to your connection string in user-secrets:
   ```
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
     "Server=...;Database=ElectCrm;Column Encryption Setting=enabled;..."
   ```

5. Run the migration:
   ```
   dotnet ef database update \
     --project src/ElectCrm.Infrastructure \
     --startup-project src/ElectCrm.Presentation
   ```

## Startup check

The application verifies column encryption is enabled at startup (implemented in a later prompt
per plan §4.8). The check validates that `Column Encryption Setting=enabled` is present on the
connection string. If absent, the application throws `InvalidOperationException` at startup and
does NOT start. There is no silent fallback to plain-text NI storage.

Verification SQL (run to confirm encryption is active):
```sql
SELECT
    c.name AS ColumnName,
    c.encryption_type_desc AS EncryptionType,
    cek.name AS EncryptionKey
FROM sys.columns c
JOIN sys.column_encryption_key_values cekv ON c.column_encryption_key_id = cekv.column_encryption_key_id
JOIN sys.column_encryption_keys cek ON cekv.column_encryption_key_id = cek.column_encryption_id
WHERE c.object_id = OBJECT_ID('WorkerProfiles')
  AND c.name = 'NationalInsuranceNumber';
```

## Compatibility note

This migration CANNOT be applied to:
- SQL Server LocalDB (does not support Always Encrypted)
- SQL Server < 2016 (Always Encrypted requires SQL Server 2016+)
- Azure SQL Database free tier / Basic tier (Always Encrypted is supported on Standard/Premium/Hyperscale)
- Any SQL Server instance where the client driver (`Microsoft.Data.SqlClient`) is not version 2.1.0+

In these environments, the migration will fail at the prerequisite check or at the ALTER COLUMN statement.
The application is designed to fail loudly in this case — there is no degraded mode without NI encryption.

## Rollback (Down)

The Down() migration:
1. Attempts to remove Always Encrypted from the NI column (safe only on an empty table)
2. Drops all three tables in reverse FK dependency order

**If WorkerProfiles contains data**, rolling back requires:
1. Decrypt the NationalInsuranceNumber column using SSMS Always Encrypted tooling or PowerShell
2. Then run `dotnet ef database update <previous-migration-name>`

Do not attempt an automated rollback with data present — data loss risk.
