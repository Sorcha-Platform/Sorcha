# Quickstart: Cloud KMS Key Management

## Development (Docker — no KMS required)

No changes needed for development. The default provider is `Local`, which uses in-memory key storage with no cloud dependency. All wallet operations work as before.

```bash
docker-compose up -d
# Wallets created with SigningMode=Local by default
```

## Azure Key Vault Setup

### 1. Create Key Vault

```bash
az keyvault create \
  --name sorcha-keyvault \
  --resource-group rg-sorcha \
  --location uksouth \
  --sku premium  # Premium required for HSM-backed keys
```

### 2. Create Managed Identity (if not using Container Apps system identity)

```bash
az identity create \
  --name sorcha-wallet-identity \
  --resource-group rg-sorcha
```

### 3. Grant Key Vault Access

```bash
az keyvault set-policy \
  --name sorcha-keyvault \
  --object-id <managed-identity-principal-id> \
  --key-permissions create get unwrapKey wrapKey sign verify
```

### 4. Configure Wallet Service

In `appsettings.Production.json` or environment variables:

```json
{
  "EncryptionProvider": {
    "Type": "AzureKeyVault",
    "AzureKeyVault": {
      "VaultUri": "https://sorcha-keyvault.vault.azure.net/",
      "UseManagedIdentity": true,
      "DekCacheTtlMinutes": 30,
      "AllowStaleDeksOnOutage": true
    }
  },
  "WalletKeyManagement": {
    "DefaultSigningMode": "Local",
    "KmsResidentPaths": [
      "m/44'/0'/0'/0/100",
      "m/44'/0'/0'/0/101",
      "m/44'/0'/0'/0/102",
      "m/44'/0'/0'/0/103"
    ],
    "AllowSigningModeOverride": true
  }
}
```

### 5. Verify

```bash
# Create a standard wallet (Local mode — envelope encryption with KV-wrapped DEK)
curl -X POST http://localhost/api/v1/wallets \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"algorithm": "Ed25519", "derivationPath": "m/44'/0'/0'/0/0"}'
# Response includes: "signingMode": "Local"

# Create a KMS-resident wallet (P-256 only)
curl -X POST http://localhost/api/v1/wallets \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"algorithm": "P256", "derivationPath": "m/44'/0'/0'/0/100"}'
# Response includes: "signingMode": "KmsResident", "kmsKeyId": "https://sorcha-keyvault.vault.azure.net/keys/..."
```

## Key Operations

| Operation | Local Mode | KMS-Resident Mode |
|-----------|-----------|-------------------|
| Create wallet | Derive key locally, encrypt with DEK, wrap DEK in KV | Create key in KV, retrieve public key |
| Sign transaction | Unwrap DEK (cached), decrypt key, sign locally | KV signs directly (~100-500ms) |
| Verify signature | Local verification (public key) | Local verification (public key stored locally) |
| Key rotation | Decrypt with old DEK, re-encrypt with new DEK | Managed by Key Vault (auto-rotation policies) |
