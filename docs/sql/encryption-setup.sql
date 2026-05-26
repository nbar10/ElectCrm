-- =============================================================================
-- Plan 09 §4.6 — Always Encrypted prerequisites for WorkerProfiles.NationalInsuranceNumber
-- =============================================================================
-- Run this script BEFORE applying the AddWorkerOnboardingSlice migration.
-- Idempotent: safe to run multiple times.
-- The CMK and CEK must exist in the TARGET database before the migration runs.
-- =============================================================================

-- PRODUCTION: Azure Key Vault-backed CMK
-- Replace <key-vault-url> with the full key path from Azure Key Vault.
-- Format: https://<vault-name>.vault.azure.net/keys/<key-name>/<key-version>
-- The Key Vault must be accessible from the machine running this script
-- (App Service managed identity in prod; developer credentials via az login in dev).

IF NOT EXISTS (
    SELECT 1 FROM sys.column_master_keys WHERE name = N'CMK_AzureKeyVault'
)
BEGIN
    -- Replace '<key-vault-url>' with the actual Key Vault key URL before running.
    CREATE COLUMN MASTER KEY [CMK_AzureKeyVault]
    WITH (
        KEY_STORE_PROVIDER_NAME = N'AZURE_KEY_VAULT',
        KEY_PATH = N'<key-vault-url>'
    );
    PRINT N'Created CMK_AzureKeyVault.';
END
ELSE
BEGIN
    PRINT N'CMK_AzureKeyVault already exists — skipped.';
END;
GO

-- COLUMN ENCRYPTION KEY (CEK)
-- The CEK is generated and wrapped by the CMK. The ENCRYPTED_VALUE below
-- is a placeholder — you must generate the actual value using one of:
--
--   Option 1 (SSMS): Use the Always Encrypted wizard to create the CEK
--     and export the T-SQL. Paste the generated CREATE COLUMN ENCRYPTION KEY
--     statement with its ENCRYPTED_VALUE here.
--
--   Option 2 (PowerShell / SqlServer module):
--     $cmkSettings = New-SqlCertificateStoreColumnMasterKeySettings -CertificateStoreLocation "CurrentUser" -Thumbprint "<thumbprint>"
--     $cekValue = New-SqlColumnEncryptionKeyEncryptedValue -TargetColumnMasterKeySettings $cmkSettings
--     Or use New-SqlAzureKeyVaultColumnMasterKeySettings for Azure Key Vault.
--
--   Option 3 (generate-cek.ps1): The team PowerShell script at scripts/generate-cek.ps1
--     wraps the CEK using the Azure Key Vault CMK and prints the ENCRYPTED_VALUE.
--     Run once per environment and update this file with the result.

IF NOT EXISTS (
    SELECT 1 FROM sys.column_encryption_keys WHERE name = N'CEK_WorkerNI'
)
BEGIN
    -- REPLACE the ENCRYPTED_VALUE below with the actual wrapped CEK bytes
    -- from your CMK. Do not commit placeholder values.
    --
    -- Example structure (actual bytes will be much longer):
    -- CREATE COLUMN ENCRYPTION KEY [CEK_WorkerNI]
    -- WITH VALUES (
    --     COLUMN_MASTER_KEY = [CMK_AzureKeyVault],
    --     ALGORITHM = N'RSA_OAEP',
    --     ENCRYPTED_VALUE = 0x01700000016C...  -- generated value here
    -- );
    PRINT N'CEK_WorkerNI does not exist. Generate the encrypted value and uncomment the CREATE statement above.';
    PRINT N'See docs/sql/encryption-setup.sql comments for instructions.';
END
ELSE
BEGIN
    PRINT N'CEK_WorkerNI already exists — skipped.';
END;
GO

-- =============================================================================
-- DEVELOPMENT SETUP (macOS — Azure Key Vault with developer credentials)
-- =============================================================================
-- macOS does not support the Windows Certificate Store (MSSQL_CERTIFICATE_STORE).
-- Use Azure Key Vault for all environments, including local development.
--
-- One-time developer setup:
--   1. az login                              (authenticate to Azure)
--   2. Confirm dev Key Vault access:
--      az keyvault key show \
--        --vault-name <elect-dev-vault> \
--        --name <key-name>
--   3. Run scripts/generate-cek.ps1 (if CEK not yet created for this environment)
--   4. Run this script against your local SQL Server instance with the dev vault URL
--   5. Verify: SELECT name FROM sys.column_master_keys;
--              SELECT name FROM sys.column_encryption_keys;
--   6. Run the migration: dotnet ef database update --project src/ElectCrm.Infrastructure
--                                                   --startup-project src/ElectCrm.Presentation
--
-- Connection string requirement for local dev:
--   Add to user-secrets or appsettings.Development.json:
--   "ConnectionStrings:DefaultConnection":
--     "Server=...;Database=ElectCrm;Column Encryption Setting=enabled;..."
-- =============================================================================
