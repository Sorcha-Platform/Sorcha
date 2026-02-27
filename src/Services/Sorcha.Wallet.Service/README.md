# Sorcha Wallet Service

**Version**: 1.0.0
**Status**: 90% Complete (Core Complete, Production Infrastructure Pending)
**Framework**: .NET 10.0
**Architecture**: Microservice

---

## Overview

The **Wallet Service** provides enterprise-grade cryptographic wallet management with Hierarchical Deterministic (HD) wallet support, enabling secure key generation, transaction signing, and payload encryption/decryption. It implements **BIP32/BIP39/BIP44 standards** for deterministic key derivation and supports multiple cryptographic algorithms (ED25519, NISTP256, RSA-4096).

This service acts as the cryptographic foundation for:
- **Secure key management** with encrypted private keys
- **HD wallet architecture** enabling unlimited addresses from a single mnemonic
- **Client-side address derivation** (BIP44) for privacy and scalability
- **Multi-algorithm cryptography** supporting different blockchain requirements
- **Access delegation** with granular permission control

### Key Features

- **HD Wallet Creation**: BIP39 mnemonic generation (12/15/18/21/24 words) with optional passphrase
- **Wallet Recovery**: Restore wallets from mnemonic phrase (disaster recovery)
- **Client-Side BIP44 Address Derivation**: Privacy-preserving address generation without server communication
- **Multi-Algorithm Support**: ED25519 (fast signatures), NISTP256 (secp256r1), RSA-4096 (legacy compatibility)
- **Transaction Signing**: Cryptographically sign transactions for blockchain submission
- **Payload Encryption/Decryption**: Selective data disclosure with asymmetric encryption
- **Access Delegation**: Grant read/write access to other identities (Owner, ReadWrite, ReadOnly roles)
- **Private Key Encryption at Rest**: AES-256-GCM encryption for stored keys
- **Multi-Account Support**: BIP44 account hierarchy (m/44'/coin'/account'/change/index)
- **Gap Limit Enforcement**: BIP44-compliant 20-address limit for receive/change chains
- **Address Management**: Track used/unused addresses, mark as used, query by account

---

## Architecture

### HD Wallet Structure (BIP44)

```
Mnemonic (12-24 words)
    └── Seed (512 bits)
        └── Master Key (m/)
            └── Purpose (m/44')
                └── Coin Type (m/44'/coin')
                    └── Account (m/44'/coin'/0', m/44'/coin'/1', ...)
                        ├── External Chain (m/44'/coin'/0'/0)  [Receive addresses]
                        │   ├── Address 0 (m/44'/coin'/0'/0/0)
                        │   ├── Address 1 (m/44'/coin'/0'/0/1)
                        │   └── ...
                        └── Internal Chain (m/44'/coin'/0'/1)  [Change addresses]
                            ├── Address 0 (m/44'/coin'/0'/1/0)
                            └── ...
```

### Components

```
Wallet Service
├── API Endpoints
│   ├── Wallets (CRUD, sign, encrypt, decrypt)
│   ├── HD Addresses (register, list, mark-used)
│   ├── Accounts (list BIP44 accounts)
│   ├── Access Control (grant, revoke, check)
│   └── Gap Status (BIP44 compliance)
├── Cryptography Layer
│   ├── Sorcha.Cryptography (multi-algorithm)
│   ├── NBitcoin (BIP32/BIP39/BIP44)
│   └── AES-256-GCM (key encryption)
├── Repositories
│   ├── Wallet Repository (in-memory, EF Core pending)
│   └── Address Repository (in-memory)
└── External Integrations
    ├── Azure Key Vault (planned for production)
    └── Tenant Service (authentication - planned)
```

### Data Flow

```
Client → Wallet API → [Create Wallet]
      ↓
Generate Mnemonic (BIP39) → Derive Master Key (BIP32) → Encrypt Private Key (AES-256-GCM)
      ↓
Store in Repository → Return Public Key & Address
      ↓
Client-Side Derivation → [Derive Child Address] → Register with Wallet Service
      ↓
Transaction Signing → Wallet Service → [Sign Transaction] → Return Signature
```

---

## Quick Start

### Prerequisites

- **.NET 10 SDK** or later
- **Docker Desktop** (optional, for production dependencies)
- **Git**

### 1. Clone and Navigate

```bash
git clone https://github.com/yourusername/Sorcha.git
cd Sorcha/src/Services/Sorcha.Wallet.Service
```

### 2. Set Up Configuration

The service uses `appsettings.json` for configuration. For local development, defaults are pre-configured.

### 3. Run the Service

```bash
dotnet run
```

Service will start at:
- **HTTPS**: `https://localhost:7084`
- **HTTP**: `http://localhost:5084`
- **Scalar API Docs**: `https://localhost:7084/scalar`

---

## Configuration

### appsettings.json Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Wallet": {
    "EncryptionProvider": "Local",
    "KeyDerivationIterations": 100000,
    "EnableClientSideDerivation": true,
    "GapLimit": 20
  },
  "OpenTelemetry": {
    "ServiceName": "Sorcha.Wallet.Service",
    "ZipkinEndpoint": "http://localhost:9411"
  }
}
```

### Environment Variables

For production deployment:

```bash
# Encryption provider (Local, AzureKeyVault, AwsKms)
WALLET__ENCRYPTIONPROVIDER="AzureKeyVault"

# Azure Key Vault settings (if using AzureKeyVault)
AZURE__KEYVAULTURL="https://your-vault.vault.azure.net/"

# Database connection (when EF Core is implemented)
CONNECTIONSTRINGS__WALLETDB="Server=.;Database=SorchaWallet;Trusted_Connection=True;"

# Observability
OPENTELEMETRY__ZIPKINENDPOINT="https://zipkin.yourcompany.com"
```

---

## API Endpoints

### Wallet Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/wallets/` | Create a new HD wallet |
| POST | `/api/v1/wallets/recover` | Recover wallet from mnemonic |
| GET | `/api/v1/wallets/` | List wallets for current user |
| GET | `/api/v1/wallets/{address}` | Get wallet by address |
| PATCH | `/api/v1/wallets/{address}` | Update wallet metadata (name, tags) |
| DELETE | `/api/v1/wallets/{address}` | Delete wallet (soft delete) |
| POST | `/api/v1/wallets/{address}/sign` | Sign a transaction |
| POST | `/api/v1/wallets/{address}/decrypt` | Decrypt a payload |
| POST | `/api/v1/wallets/{address}/encrypt` | Encrypt a payload |

### HD Address Management (BIP44)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/wallets/{address}/addresses` | Register client-derived address |
| GET | `/api/v1/wallets/{address}/addresses` | List wallet addresses |
| GET | `/api/v1/wallets/{address}/addresses/{id:guid}` | Get specific address details |
| PATCH | `/api/v1/wallets/{address}/addresses/{id:guid}` | Update address metadata |
| POST | `/api/v1/wallets/{address}/addresses/{id:guid}/mark-used` | Mark address as used |
| GET | `/api/v1/wallets/{address}/accounts` | List BIP44 accounts |
| GET | `/api/v1/wallets/{address}/gap-status` | Get gap limit compliance status |

### Access Control (Delegation)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/wallets/{walletAddress}/access` | Grant access to another identity |
| GET | `/api/v1/wallets/{walletAddress}/access` | List active access grants |
| DELETE | `/api/v1/wallets/{walletAddress}/access/{subject}` | Revoke access |
| GET | `/api/v1/wallets/{walletAddress}/access/{subject}/check` | Check if subject has access |

For full API documentation with request/response schemas, open **Scalar UI** at `https://localhost:7084/scalar`.

---

## Development

### Project Structure

```
Sorcha.Wallet.Service/
├── Program.cs                          # Service entry point
├── Endpoints/
│   ├── WalletEndpoints.cs              # Wallet CRUD, sign, encrypt
│   ├── AddressEndpoints.cs             # HD address management
│   ├── AccountEndpoints.cs             # BIP44 account operations
│   └── AccessEndpoints.cs              # Access delegation
├── Services/
│   ├── WalletService.cs                # Business logic
│   ├── CryptographyService.cs          # Signing, encryption
│   ├── AddressDerivationService.cs     # BIP44 derivation
│   └── AccessControlService.cs         # Access management
├── Repositories/
│   ├── IWalletRepository.cs            # Repository interfaces
│   ├── WalletRepository.cs             # In-memory implementation
│   ├── IAddressRepository.cs
│   └── AddressRepository.cs
├── Models/
│   ├── Wallet.cs                       # Domain models
│   ├── WalletAddress.cs
│   ├── AccessGrant.cs
│   └── Account.cs
└── appsettings.json                    # Configuration

External Libraries:
├── Sorcha.Cryptography/                # Multi-algorithm crypto
├── Sorcha.Wallet.Core/                 # Core wallet logic
└── NBitcoin/                           # BIP32/BIP39/BIP44
```

### Running Tests

```bash
# Run all Wallet Service tests
dotnet test tests/Sorcha.Wallet.Service.Tests

# Run API integration tests
dotnet test tests/Sorcha.Wallet.Service.Api.Tests

# Run with coverage
dotnet test tests/Sorcha.Wallet.Service.Tests --collect:"XPlat Code Coverage"

# Watch mode
dotnet watch test --project tests/Sorcha.Wallet.Service.Tests
```

### Code Coverage

**Current Coverage**: ~87%
**Tests**: 111 unit tests, 35 integration tests
**Lines of Code**: ~8,000 LOC

```bash
# Generate coverage report
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage -reporttypes:Html
```

---

## HD Wallet Workflow

### 1. Create Wallet

```http
POST /api/v1/wallets/
Content-Type: application/json

{
  "name": "My HD Wallet",
  "algorithm": "ED25519",
  "wordCount": 24,
  "passphrase": "optional-bip39-passphrase"
}
```

**Response:**
```json
{
  "address": "did:sorcha:wallet:abc123",
  "mnemonic": "word1 word2 word3 ... word24",
  "publicKey": "base64-encoded-public-key",
  "algorithm": "ED25519"
}
```

**⚠️ Important**: Save the mnemonic in a secure location. It cannot be retrieved later.

### 2. Client-Side Address Derivation (BIP44)

```typescript
import { HDKey } from '@scure/bip32';
import { mnemonicToSeedSync } from '@scure/bip39';

// Derive child address client-side
const mnemonic = "word1 word2 ... word24";
const seed = mnemonicToSeedSync(mnemonic, passphrase);
const masterKey = HDKey.fromMasterSeed(seed);

// BIP44 path: m/44'/0'/0'/0/0 (first receive address)
const childKey = masterKey.derive("m/44'/0'/0'/0/0");
const address = childKey.publicKey;
```

### 3. Register Address with Wallet Service

```http
POST /api/v1/wallets/{walletAddress}/addresses
Content-Type: application/json

{
  "bip44Path": "m/44'/0'/0'/0/0",
  "publicKey": "base64-encoded-public-key",
  "purpose": 44,
  "coinType": 0,
  "account": 0,
  "change": 0,
  "addressIndex": 0,
  "label": "First receive address"
}
```

### 4. Mark Address as Used

```http
POST /api/v1/wallets/{walletAddress}/addresses/{addressId}/mark-used
```

### 5. Check Gap Limit Status

```http
GET /api/v1/wallets/{walletAddress}/gap-status
```

**Response:**
```json
{
  "gapLimit": 20,
  "accounts": [
    {
      "account": 0,
      "receiveChain": {
        "unusedCount": 5,
        "lastUsedIndex": 14,
        "isCompliant": true
      },
      "changeChain": {
        "unusedCount": 18,
        "lastUsedIndex": 1,
        "isCompliant": true
      }
    }
  ]
}
```

---

## Integration with Other Services

### Blueprint Service Integration

The Wallet Service is called by the Blueprint Service for:
- **Transaction Signing**: Sign action transactions before blockchain submission
- **Payload Encryption**: Encrypt selective disclosure payloads
- **Payload Decryption**: Decrypt received action data

**Communication**: HTTP REST API

### Tenant Service Integration (Planned)

Future integration for:
- **JWT Authentication**: Validate bearer tokens
- **User Context**: Retrieve wallet ownership information
- **Multi-Tenant Isolation**: Wallet scoping by organization

---

## Security Considerations

### Private Key Protection

- **At-Rest Encryption**: All private keys encrypted with AES-256-GCM
- **Encryption Key Storage**:
  - **Development**: Local in-memory encryption (NOT secure - dev/test only)
  - **Production**: Azure Key Vault, AWS KMS, or Google Cloud KMS
- **Mnemonic Handling**: Never stored; only shown once during wallet creation
- **Memory Protection**: Sensitive data cleared from memory after use

### Encryption Provider Configuration

**⚠️ IMPORTANT**: The default `LocalEncryptionProvider` is **NOT suitable for production** as it stores encryption keys in memory only and they are lost on service restart.

#### Development/Testing Configuration

The current Docker Compose setup uses `LocalEncryptionProvider` for development:

```bash
# Warning message in logs (expected in development):
warn: Sorcha.Wallet.Core.Encryption.Providers.LocalEncryptionProvider[0]
      LocalEncryptionProvider initialized. This provider is for development only.
```

**Limitations**:
- Keys stored in memory only (lost on restart)
- No key backup or recovery
- Not suitable for production use
- Wallets created in development cannot be migrated to production

#### Production Configuration Options

**Option 1: Azure Key Vault (Recommended for Azure deployments)**

```json
{
  "Wallet": {
    "EncryptionProvider": "AzureKeyVault",
    "AzureKeyVault": {
      "VaultUrl": "https://your-vault.vault.azure.net/",
      "UseManagedIdentity": true
    }
  }
}
```

Docker Compose configuration:
```yaml
wallet-service:
  environment:
    Wallet__EncryptionProvider: "AzureKeyVault"
    Wallet__AzureKeyVault__VaultUrl: "https://your-vault.vault.azure.net/"
    Wallet__AzureKeyVault__UseManagedIdentity: "true"
    AZURE_TENANT_ID: "${AZURE_TENANT_ID}"
    AZURE_CLIENT_ID: "${AZURE_CLIENT_ID}"
    AZURE_CLIENT_SECRET: "${AZURE_CLIENT_SECRET}"
```

**Option 2: AWS KMS**

```json
{
  "Wallet": {
    "EncryptionProvider": "AwsKms",
    "AwsKms": {
      "Region": "us-east-1",
      "KeyId": "arn:aws:kms:us-east-1:123456789012:key/your-key-id",
      "UseIamRole": true
    }
  }
}
```

**Option 3: Google Cloud KMS**

```json
{
  "Wallet": {
    "EncryptionProvider": "GcpKms",
    "GcpKms": {
      "ProjectId": "your-project-id",
      "LocationId": "global",
      "KeyRingId": "sorcha-keys",
      "KeyId": "wallet-encryption-key"
    }
  }
}
```

#### Migration from Development to Production

**WARNING**: Wallets created with `LocalEncryptionProvider` **CANNOT** be migrated to production HSM backends because:
1. The encryption keys are stored in memory and lost on restart
2. There is no key export mechanism for security reasons
3. Private keys are encrypted with ephemeral keys

**Production Deployment Checklist**:
- [ ] Configure Azure Key Vault, AWS KMS, or GCP Cloud KMS
- [ ] Set up Managed Identity or IAM Role for the service
- [ ] Test encryption/decryption with the HSM provider
- [ ] Ensure DataProtection keys are stored in a shared volume
- [ ] Verify audit logging is enabled
- [ ] Test wallet creation and signing operations
- [ ] Confirm private keys are never logged
- [ ] Set up key rotation policies in your HSM provider

For detailed HSM configuration, see: [Hardware Cryptographic Storage Feature Spec](../../specs/001-hardware-crypto-enclaves/spec.md)

### Access Control

Three access levels:
- **Owner**: Full control (sign, encrypt, decrypt, grant access, delete)
- **ReadWrite**: Sign and encrypt/decrypt (no access management)
- **ReadOnly**: View wallet information only (no cryptographic operations)

### Authentication

- **Current**: Development mode (no authentication required)
- **Production**: JWT bearer token authentication (issued by Tenant Service)

### Best Practices

- ✅ Always use HTTPS in production
- ✅ Store mnemonics offline (paper wallets, hardware wallets)
- ✅ Use passphrases for additional mnemonic protection
- ✅ Rotate access grants periodically
- ✅ Enable Azure Key Vault or AWS KMS for production key management

---

## Deployment

### .NET Aspire (Development)

The Wallet Service is registered in the Aspire AppHost:

```csharp
var walletService = builder.AddProject<Projects.Sorcha_Wallet_Service>("wallet-service")
    .WithReference(redis);
```

Start the entire platform:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

Access Aspire Dashboard: `http://localhost:15888`

### Docker

```bash
# Build Docker image
docker build -t sorcha-wallet-service:latest -f src/Services/Sorcha.Wallet.Service/Dockerfile .

# Run container
docker run -d \
  -p 7084:8080 \
  -e Wallet__EncryptionProvider="Local" \
  --name wallet-service \
  sorcha-wallet-service:latest
```

### Azure Deployment

Deploy to Azure Container Apps with:
- **Key Management**: Azure Key Vault for encryption keys
- **Database**: Azure SQL Database (when EF Core is implemented)
- **Secrets**: Managed Identity for Key Vault access
- **Observability**: Application Insights integration

---

## Observability

### Logging (Serilog + Seq)

Structured logging with Serilog:

```csharp
Log.Information("Wallet {WalletAddress} created with algorithm {Algorithm}", address, algorithm);
```

**Log Sinks**:
- Console (structured output via Serilog)
- OTLP → Aspire Dashboard (centralized log aggregation)

**Security**: Private keys and mnemonics are NEVER logged.

### Tracing (OpenTelemetry + Zipkin)

Distributed tracing with OpenTelemetry:

```bash
# View traces in Zipkin
open http://localhost:9411
```

**Traced Operations**:
- Wallet creation/recovery
- Signing operations
- Address derivation

### Metrics (Prometheus)

Metrics exposed at `/metrics`:
- Wallet creation rate
- Signing operation latency
- Access grant count
- Address registration rate

---

## Troubleshooting

### Common Issues

**Issue**: Mnemonic recovery fails with "Invalid mnemonic"
**Solution**: Ensure the mnemonic phrase is exactly as generated (correct word order, no typos). Verify passphrase if one was used.

**Issue**: "Gap limit exceeded" error when registering address
**Solution**: Mark some addresses as used first, or create a new account:

```http
POST /api/v1/wallets/{address}/addresses
{
  "account": 1,  // New account
  "change": 0,
  "addressIndex": 0
}
```

**Issue**: Signing fails with "Wallet not found"
**Solution**: Verify the wallet address and that the wallet hasn't been soft-deleted.

**Issue**: Encryption key not accessible (Azure Key Vault)
**Solution**: Verify Managed Identity permissions:

```bash
# Grant Key Vault access
az keyvault set-policy --name your-vault \
  --object-id <managed-identity-id> \
  --key-permissions get unwrapKey wrapKey
```

### Debug Mode

Enable detailed logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Sorcha.Wallet.Service": "Trace",
      "Sorcha.Cryptography": "Trace"
    }
  }
}
```

---

## Pending Production Features

### Priority 1 (Required for Production)
- [ ] **EF Core Repository**: Replace in-memory repository with SQL Server/PostgreSQL
- [ ] **Azure Key Vault Integration**: Production-grade key encryption
- [ ] **JWT Authentication**: Integration with Tenant Service
- [ ] **Rate Limiting**: Prevent brute-force attacks

### Priority 2 (Nice to Have)
- [ ] **AWS KMS Support**: Alternative to Azure Key Vault
- [ ] **Hardware Wallet Integration**: Ledger, Trezor support
- [ ] **Audit Logging**: Comprehensive operation logging
- [ ] **Backup/Restore**: Encrypted wallet backup system

---

## Contributing

### Development Workflow

1. **Create a feature branch**: `git checkout -b feature/your-feature`
2. **Make changes**: Follow C# coding conventions
3. **Write tests**: Maintain >85% coverage
4. **Run tests**: `dotnet test`
5. **Format code**: `dotnet format`
6. **Commit**: `git commit -m "feat: your feature description"`
7. **Push**: `git push origin feature/your-feature`
8. **Create PR**: Reference issue number

### Code Standards

- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use async/await for I/O operations
- Add XML documentation for public APIs
- Include unit tests for all business logic
- Never log sensitive data (keys, mnemonics, passphrases)

---

## Resources

- **Specification**: [.specify/specs/sorcha-wallet-service.md](.specify/specs/sorcha-wallet-service.md)
- **API Reference**: [Scalar UI](https://localhost:7084/scalar)
- **Development Status**: [docs/wallet-service-status.md](../../docs/wallet-service-status.md)
- **Architecture**: [docs/architecture.md](../../docs/architecture.md)
- **BIP39 Standard**: [Bitcoin BIPs](https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki)
- **BIP44 Standard**: [Bitcoin BIPs](https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki)
- **OpenAPI Spec**: `https://localhost:7084/openapi/v1.json`

---

## License

Apache License 2.0 - See [LICENSE](../../LICENSE) for details.

---

**Last Updated**: 2025-11-23
**Maintained By**: Sorcha Contributors
**Status**: ⚠️ 90% Complete (Core Complete, Production Infrastructure Pending)
