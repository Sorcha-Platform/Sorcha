# Sorcha Wallet Service

**Version**: 1.0.0
**Status**: 98% Complete
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
├── Repositories (F113-audited — persistent in Prod/Staging)
│   ├── IWalletRepository → EfCoreWalletRepository (PostgreSQL)
│   └── IHolderAddressLookup (citizen address ↔ platform user)
└── External Integrations
    ├── Azure Key Vault (envelope encryption + KMS-resident signing, Feature 082)
    └── Tenant Service (JWT issuer; tiered audiences, Feature 136)
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
git clone https://github.com/Sorcha-Platform/Sorcha.git
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
  "EncryptionProvider": {
    "Type": "LinuxSecretService"
  },
  "Wallet": {
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

> **Encryption provider config lives under the `EncryptionProvider` section (`Type` property), NOT `Wallet:EncryptionProvider`.** The service binds `EncryptionProvider:Type` (`WalletServiceExtensions` → `EncryptionProviderOptions`, section name `"EncryptionProvider"`); an older `Wallet:EncryptionProvider` bare-string form appeared in these docs but is not read by the code. Valid `Type` values and per-provider settings (Azure Key Vault / AWS KMS / GCP KMS / LinuxSecretService) are documented in the **Cloud KMS Configuration** section below. Docker sets `EncryptionProvider__Type` (e.g. `LinuxSecretService`).

### Environment Variables

For production deployment:

```bash
# Encryption provider type (Local, LinuxSecretService, AzureKeyVault, AwsKms, GcpKms)
EncryptionProvider__Type="AzureKeyVault"

# Azure Key Vault settings (if Type=AzureKeyVault)
EncryptionProvider__AzureKeyVault__VaultUri="https://your-vault.vault.azure.net/"

# Database connection (PostgreSQL — EF Core / EfCoreWalletRepository)
ConnectionStrings__Wallet__Postgres="Host=postgres;Database=sorcha_wallet;Username=sorcha;Password=…"

# Observability
OPENTELEMETRY__ZIPKINENDPOINT="https://zipkin.yourcompany.com"
```

---

## API Endpoints

### Wallet ownership binding (required on every wallet-scoped route)

`CanManageWallets` is **not** an ownership check. It asserts only that the token carries some
non-empty `org_id` **or** is a service token (`Extensions/AuthenticationExtensions.cs`). Consumer-tier
citizen tokens carry `org_id` (their home org, Feature 136), so **every authenticated citizen
satisfies it — for every wallet**. It answers "is this a legitimate wallet-API caller at all", never
"may they act on *this* wallet".

Any route whose template names a wallet must therefore also apply **`.RequireWalletOwnership()`**
(`Authorization/WalletOwnershipGate.cs`):

```csharp
var group = app.MapGroup("/api/v1/wallets/{walletAddress}/credentials")
    .RequireAuthorization("CanManageWallets")   // is a wallet-API caller
    .RequireWalletOwnership();                  // may act on THIS wallet
```

The gate: **service tokens pass** (issuance legitimately targets the *issuing org's* wallet while the
recipient is another citizen); otherwise the caller identity — `platform_user_id` falling back to
`NameIdentifier`, matching what `WalletEndpoints.GetCurrentUser` stamps as `Wallets.Owner` — must
equal the wallet's owner, else **403** with a `SEC-AUDIT` log. Unknown wallet is **404** for
everyone, so the response never reveals existence. A route carrying the gate but **no** wallet
address in its template **fails closed** — a mis-wiring must not look like a working control.

It reads both `{walletAddress}` and `{address}`, because the routes are not consistently named.

**Why a shared filter and not a check per handler:** the G1 defect (2026-07-29 catch-up security
review) was not a subtly wrong check — it was **no check at all**, in several groups at once, while
four sibling routes in `WalletEndpoints` did verify ownership inline. `RequireWalletOwnership()` also
stamps marker metadata so `WalletOwnershipWiringTests` can assert every wallet-scoped route carries
it; an endpoint filter is otherwise invisible to route metadata, which would make "the new group
forgot the gate" undetectable.

**Gated today:** the credentials group, the delegation (`/access`) group, and `PATCH` / `DELETE`
`/api/v1/wallets/{address}`.

**Not yet gated (tracked):** the HD-address, accounts, gap-status and encapsulate/decapsulate routes.
Each needs an individual decision rather than a blanket sweep — `encrypt`, for instance, deliberately
lets a caller encrypt *to* a wallet they do not own, and `GET /{address}` intentionally also accepts
an active delegate. `sign`, `decrypt`, `decapsulate` and `GET /{address}` already verify ownership
inline.

**#1397 — the service-token bypass on `sign` is narrowed for `validator:*`-owned wallets.** A
`validator:*`-owned system wallet holds the docket-signing / SSR-owner key, so a service token may
only sign with one when it is the Validator service principal itself (`client_id == "validator-service"`,
per `Sorcha.Tenant.Service`'s seeded `ServicePrincipal`). Any other service token targeting a
`validator:*` wallet gets **403** with a `SEC-AUDIT` log; the bypass for every other (non-system)
wallet — e.g. Blueprint Service signing an issuing org's wallet during credential issuance — is
unchanged.

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

### Citizen Wallet — Server-Custody KB-JWT Signing (#1195 Phase 2)

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/wallet/presentations/sign-kb` | Sign a KB-JWT signing input with the **authenticated caller's** slot-108 holder key (consumer-tier, `Strict` rate limit) |

Lets the wallet PWA present a holder-`cnf` credential (the web-issued `AssuredIdentityCredential`
root) with the holder private key staying in server custody. The signing wallet is resolved from
the citizen JWT only — the body carries no wallet address. The endpoint signs **only** inputs whose
decoded header is `typ: "kb+jwt"` (never a general-purpose signing oracle — the same key signs
device delegation credentials), and refuses with a named 400 when the header `alg` cannot match the
holder key's real algorithm (EC P-256 → `ES256`, OKP Ed25519 → `EdDSA`). The rest of the citizen
wallet surface (`/api/v1/wallet/*`, Feature 114) is documented in `docs/reference/API-DOCUMENTATION.md`.

### Ethereum Auxiliary Identity (Features 180 / 181)

A wallet exposes an **auxiliary Ethereum identity** — a secp256k1 key derived on demand from its existing
seed at `m/44'/60'/0'/0/{index}` (the primary algorithm is unchanged; the key is never returned).

**Prove-control (Feature 180) — EIP-191 / SIWE:**

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v1/wallets/{walletAddress}/ethereum-address` | The wallet's EIP-55 Ethereum address |
| POST | `/api/v1/wallets/{walletAddress}/siwe/sign` | Sign a SIWE (EIP-4361) prove-control message |
| POST | `/api/v1/siwe/verify` | Verify an inbound SIWE proof (Sorcha as relying party) |

**Transacting (Feature 181) — native ETH transfers, EIP-1559 (type-2), server-side only:**

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/wallets/{walletAddress}/ethereum/transactions` | Send a native ETH transfer; returns the tx hash (`CanTransactEthereum`) |
| POST | `/api/v1/wallets/{walletAddress}/ethereum/transactions/preview` | Read-only cost preview — nonce/gas/fees/total, no broadcast (`CanTransactEthereum`) |
| GET | `/api/v1/ethereum/transactions/{chainId}/{txHash}` | Receipt status: `pending` / `success` / `reverted` |

Transacting is **gated** by `Ethereum:Transactions` policy (`EnabledChainIds` — Sepolia + Holešky by
default, `AllowMainnet` master switch off by default, `MaxValueWei` per-tx cap, `MaxFeePerGasWei` ceiling)
and the `CanTransactEthereum` authorization policy. Amounts are wei as decimal strings. Encoding is
pure-managed RLP + EIP-1559 (no Nethereum); the per-chain **write-capable** RPC endpoint is read from the
shared `DidResolver:Ethr:Rpc:{chainId}` cascade. Fail-closed: any policy/RPC/estimate failure refuses the
transfer with no broadcast. See `docs/superpowers/specs/2026-07-11-ethereum-verify-phase4-design.md`.

For full API documentation with request/response schemas, open **Scalar UI** at `https://localhost:7084/scalar`.

---

## Org Key Derivation (Feature 083)

Organisation-level HD key derivation using Sorcha-specific BIP32 paths (`m/0x534F52'/org'/dept'/user'/usage/index`). Enables org-controlled key lifecycle without individual mnemonic management.

### Custody Modes

| Mode | Description | Status |
|------|-------------|--------|
| **Custodial** | Full key in KMS, no device share — service accounts/automation | Implemented |
| **Co-signed** | Server share + device share — standard user operations | Schema only |
| **Self-custody** | Full key on device, optional recovery escrow — external wallets | Schema only |

### Org Key Derivation Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/wallets/org/{orgId}/master-key` | Provision org master key (one-shot, returns mnemonic once) |
| POST | `/api/wallets/org/{orgId}/derive-key` | Derive user key (idempotent) |
| POST | `/api/wallets/org/{orgId}/keys/{derivedKeyId}/rotate` | Rotate key (new at next index, old decrypt-only) |
| DELETE | `/api/wallets/org/{orgId}/keys/{derivedKeyId}` | Revoke key (wallet locked, DID event for identity keys) |

### Key Usage Types

| Value | Name | Purpose |
|-------|------|---------|
| 0 | Identity | DID identity keys |
| 1 | VCIssuance | Verifiable credential signing |
| 2 | Governance | Register governance operations |
| 3 | Communications | Encrypted communications |
| 4 | ServiceAuth | Service-to-service authentication |

### Key Entities

- **OrgMasterKey**: Organisation root seed, encrypted at rest via Key Protection Provider (KPP), one per org
- **DerivedKeyRecord**: User key derived from org master, tracks derivation path, usage type, index, and status (Active/Rotated/Revoked)

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
├── Services/                           # PresentationRequestService, EncryptionEventBridge, Implementation/, Interfaces/
├── GrpcServices/                       # gRPC surface (wallet notifications)
├── Mappers/  Validators/  Protos/
├── Models/                             # Request/response DTOs
└── appsettings.json

External Libraries:
├── Sorcha.Wallet.Contracts/            # Canonical wallet HTTP DTOs
├── Sorcha.Cryptography/                # Multi-algorithm crypto
├── Sorcha.Wallet.Core/                 # Persistence lives HERE: Repositories/EfCoreWalletRepository (PostgreSQL sorcha_wallet)
│                                       #   + Repositories/Implementation/InMemoryWalletRepository (tests) + encryption providers
├── Sorcha.Wallet.Portable/             # Portable wallet: entities, enums, derivation (NuGet package)
└── NBitcoin/                           # BIP32/BIP39/BIP44 (via Wallet.Portable)
```

> **The service has no `Repositories/` directory and no in-memory `WalletRepository`/`AddressRepository`** — those never existed here. The real store is `IWalletRepository` → `EfCoreWalletRepository` (PostgreSQL), which lives in the **`Sorcha.Wallet.Core`** library (this matches the Architecture → Repositories section above). Address registration is `IAddressRegistrationService`, not an `IAddressRepository`.

### Storage backends are chosen in one place

`WalletServiceExtensions.AddWalletDatabase` owns the Postgres-or-in-memory decision (the
`SorchaConnections` cascade: `ConnectionStrings:Wallet:Postgres` → `ConnectionStrings:Sorcha:Postgres`)
and registers **every** interface whose backend depends on it, recording each through
`IStorageRegistrationLog` (Pattern #13):

| Interface | With Postgres | Without |
|---|---|---|
| `IWalletRepository` (audited) | `EfCoreWalletRepository` | `InMemoryWalletRepository` |
| `IHolderAddressLookup` | `EfCoreHolderAddressLookup` | `InMemoryHolderAddressLookup` |

> **Do not register a `WalletDbContext`-consuming service outside this branch.** `IHolderAddressLookup`
> was registered unconditionally in `Program.cs` bound to the EF Core implementation, which cannot be
> activated without a `WalletDbContext` — and the DbContext exists only on the Postgres branch. On the
> supported no-Postgres path (Pattern #13 explicitly allows it outside Production) every endpoint that
> touched the lookup returned 500 with *"Unable to resolve service for type WalletDbContext"*,
> including `POST /api/v1/wallets`. This is what held `Sorcha.Wallet.Service.IntegrationTests` at
> **5/33** after the host-startup fix in #1339, and that suite is the service's only HTTP-level
> authorization coverage — a large part of why the wallet-ownership holes fixed in #1340 survived
> review. It is now **33/33**.
>
> `IHolderAddressLookup` is deliberately **not** on the audited list: it shares `IWalletRepository`'s
> branch, so it can never be the only audited interface on an in-memory backend — the repository's
> fail-fast already covers the identical condition. Listing it would add a second identical trigger,
> not a second safeguard.
>
> The two implementations are held to the same behaviour by `HolderAddressLookupParityTests`, which
> runs one set of assertions against both (ordinal address matching, idempotent registration,
> conflicting writes keep the first entry). An in-memory stand-in that behaved differently would turn
> 33 green integration tests into 33 tests that prove nothing about production.

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

### Credential Presentation Verification (Feature 093)

Since feature 093 (VC & presentation security fixes, HAIP prep), `POST /api/v1/presentations/{requestId}/submit` cryptographically verifies the submitted `vpToken` against the issuer's public key before any claim is considered. Claim values in the verification result come from the verified token, not from the server-side credential store. Tampered, forged, or replay tokens are rejected with specific verification errors.

### Credential Status Embedding (Feature 093)

When a credential is issued via a Blueprint action, the Blueprint Service's `ActionExecutionService` pre-allocates a status list index **before** calling the Wallet Service issue endpoint and forwards the allocation to the issue request. The Wallet Service's `CredentialEndpoints.IssueCredential` handler then embeds a `credentialStatus` claim (W3C `BitstringStatusListEntry` shape) in the signed SD-JWT VC payload in lockstep with the stored `CredentialEntity` row fields.

The `CredentialStatus:EnableEmbedding` flag lives in the **Blueprint Service**'s `appsettings.json` (not here — see `src/Services/Sorcha.Blueprint.Service/appsettings.json`). When `true` (default), the Blueprint Service pre-allocates before signing and credentials carry the embedded claim. When `false`, pre-allocation is skipped and credentials are issued without the claim — dev-environment escape hatch for stacks that do not run the status list manager.

**Non-fatal allocation failures**: if `IStatusListManager` is reachable but an allocation call throws, `ActionExecutionService` logs a warning and proceeds to issue the credential *without* the embedded claim. The credential is still valid and verifies via the spec 093 FR-010 server-side row fallback. This is a pragmatic scope deviation from spec FR-008 (which called for fail-closed) — documented in `specs/093-vc-security-fixes/tasks.md`.

The direct HTTP issuance path (`POST /api/v1/wallets/{address}/credentials/issue` called outside a Blueprint action) does not embed the claim unless the caller explicitly supplies `statusListUrl` and `statusListIndex` on the request. Callers that want a HAIP-compliant credential through this path must supply the allocation themselves.

Pre-Feature-093 credentials (no embedded `credentialStatus` claim) remain valid indefinitely and continue to verify via the server-side `CredentialEntity.StatusListUrl` / `StatusListIndex` fallback.

### Holder Binding Key (Feature 094)

Every Sorcha wallet has a deterministic holder binding key derived from the wallet seed under the `sorcha:credential-holder-binding` BIP32 purpose (index 105). This key is used to prove holder possession of a credential via a Key Binding JWT (KB-JWT) in SD-JWT VC presentations.

- **One key per wallet**, not per credential
- **Deterministic**: same mnemonic always produces the same binding key
- **Automatic**: the Wallet Service derives and signs KB-JWTs without callers managing key material

Services: `IHolderBindingKeyService` provides `GetPublicJwkAsync(walletAddress)` and `SignKbJwtAsync(walletAddress, signingInput)`.

### HAIP Issuer Classical Co-Key (Feature 094)

Wallets whose primary algorithm is PQC (ML-DSA, SLH-DSA) derive a classical co-key (ES256 by default) under the `sorcha:haip-issuer-signing` BIP32 purpose (index 106) for signing HAIP-conformant SD-JWT VCs. Wallets with a classical primary key (Ed25519, P-256) use their primary key directly.

- Requires the `HaipIssuer` capability flag on the wallet entity
- Classical-primary wallets: no new key derived, primary key used for HAIP issuance
- PQC-primary wallets: ES256 co-key derived alongside the PQC primary

Services: `IHaipIssuerCoKeyService` provides `GetSigningKeyForHaipIssuanceAsync(walletAddress)`.

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
    EncryptionProvider__Type: "AzureKeyVault"
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

For detailed HSM configuration, see: [Hardware Cryptographic Storage Feature Spec](https://github.com/Sorcha-Platform/Sorcha/blob/master/specs/001-hardware-crypto-enclaves/spec.md)

### Access Control

Three access levels:
- **Owner**: Full control (sign, encrypt, decrypt, grant access, delete)
- **ReadWrite**: Sign and encrypt/decrypt (no access management)
- **ReadOnly**: View wallet information only (no cryptographic operations)

### Authentication

JWT Bearer authentication (issued by the Tenant Service) is enforced in **all** environments. Citizen
wallet surfaces (`/api/v1/wallet`) require the **consumer** tier audience (`RequireConsumerAudience`);
key-management / admin surfaces require the **platform** or **service** tier (Feature 136).

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
  -e EncryptionProvider__Type="Local" \
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

## Cloud KMS Configuration

Feature 082 delivered full Azure Key Vault integration for wallet key storage. The service uses an **envelope encryption** model: Data Encryption Keys (DEKs) are generated locally per wallet and wrapped by the Key Protection Provider (KPP). The KPP can be a local provider (development) or Azure Key Vault (production).

### EncryptionProvider settings

Set `EncryptionProvider:Type` in `appsettings.json` (or environment variables):

| Type | When to use |
|------|-------------|
| `Local` | Development only — keys lost on restart |
| `WindowsDpapi` | Production on Windows hosts |
| `LinuxSecretService` | Production on Linux hosts / Docker |
| `AzureKeyVault` | Production on Azure — recommended |

#### Azure Key Vault (recommended for production)

```json
{
  "EncryptionProvider": {
    "Type": "AzureKeyVault",
    "DefaultKeyId": "wallet-key-2025",
    "AzureKeyVault": {
      "VaultUri": "https://your-vault.vault.azure.net/",
      "DefaultKeyName": "wallet-encryption-key",
      "UseManagedIdentity": true,
      "EnableDekCache": true,
      "DekCacheTtlMinutes": 60,
      "AllowStaleDeksOnOutage": true
    }
  }
}
```

Docker Compose / Azure Container Apps environment variables:

```bash
EncryptionProvider__Type=AzureKeyVault
EncryptionProvider__DefaultKeyId=wallet-key-2025
EncryptionProvider__AzureKeyVault__VaultUri=https://your-vault.vault.azure.net/
EncryptionProvider__AzureKeyVault__UseManagedIdentity=true
# For non-managed-identity (service principal):
AZURE_TENANT_ID=<tenant-id>
AZURE_CLIENT_ID=<client-id>
AZURE_CLIENT_SECRET=<client-secret>
```

Required Key Vault permissions for the Managed Identity / service principal:
- `keys/get`, `keys/wrapKey`, `keys/unwrapKey`
- `keys/create` (if auto-creating the KEK on first start)

### WalletKeyManagement settings

Controls DEK caching and signing mode policy:

```json
{
  "WalletKeyManagement": {
    "DefaultKeyId": "wallet-key-2025",
    "DekCacheTtlMinutes": 30,
    "DekCacheGracePeriodMinutes": 5,
    "MaxCachedDeks": 1000,
    "SigningPolicy": {
      "DefaultMode": "Local",
      "AllowLocalToKmsMigration": false,
      "AllowedKmsAlgorithms": ["ED25519", "NISTP256"]
    }
  }
}
```

`DefaultMode` options:
- `Local` — private keys are stored encrypted by envelope encryption (default).
- `KmsResident` — private keys are generated inside Azure Key Vault and never leave the HSM. Signing calls are forwarded to the KMS. Use when regulatory requirements mandate HSM-resident keys.

`AllowLocalToKmsMigration` — set to `true` to allow migrating existing `Local` wallets to `KmsResident`. The reverse is never permitted.

`AllowedKmsAlgorithms` — list of algorithm names permitted for KMS-resident keys. Empty list means all algorithms are allowed.

---

## Production Feature Status

### Completed (MVD)
- [x] **EF Core Repository**: PostgreSQL persistence via EF Core
- [x] **JWT Authentication**: Integration with Tenant Service
- [x] **Azure Key Vault Integration**: Envelope encryption + KMS-resident signing via `Sorcha.Wallet.Providers.Azure` (Feature 082)

### Deferred (Post-MVD)
- [ ] **AWS KMS / GCP KMS Support**: Multi-cloud KMS providers (deferred from Feature 082)
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

## Notification Pipeline

The Wallet Service receives inbound transaction notifications from the Register Service and delivers them to users in real-time or via digest batching.

### Pipeline Flow

```
Register Service (docket sealed)
    → gRPC: NotifyInboundTransaction
        → Resolve address → wallet → user
            → Rate limiter check
                ├── Under limit → Redis pub/sub (real-time)
                └── Over limit  → Redis sorted set (digest queue)
```

### Real-time Delivery

When an inbound transaction is detected, the notification pipeline:
1. Resolves the recipient wallet address to a user identity
2. Checks the sliding window rate limiter
3. Publishes via Redis pub/sub (`wallet:notifications` channel) for the EventsHub SignalR bridge

### Rate Limiting

A sliding window rate limiter (Redis-backed) prevents notification flooding. When the rate limit is exceeded, events are queued to the digest for later consolidated delivery. The notification rate limit is configured via the centralised `RateLimiting` section:

| Setting | Default | Description |
|---------|---------|-------------|
| `RateLimiting:NotificationRealTimePerMinute` | `100000` (dev) | Max real-time notifications per user per minute |

Production deployments should tighten this value (e.g. `10`) in `appsettings.Production.json`.

### Digest Batching

A `BackgroundService` processes accumulated notification events, groups them by blueprint, and delivers consolidated summaries. Digest frequency is determined by user preference (hourly or daily).

**Configuration** (`appsettings.json`):

| Setting | Default | Description |
|---------|---------|-------------|
| `Notifications:DigestCheckIntervalMinutes` | `5` | How often the worker checks for pending digests |
| `Notifications:DigestHourlyMinute` | `0` | Minute of the hour to send hourly digests |
| `Notifications:DigestDailyHour` | `8` | Hour of the day (UTC) to send daily digests |
| `Notifications:DigestDailyMinute` | `0` | Minute of the hour to send daily digests |

```json
{
  "RateLimiting": {
    "NotificationRealTimePerMinute": 10
  },
  "Notifications": {
    "DigestCheckIntervalMinutes": 5,
    "DigestHourlyMinute": 0,
    "DigestDailyHour": 8,
    "DigestDailyMinute": 0
  }
}
```

### Notification Preference Provider (Feature 062)

**TenantNotificationPreferenceProvider** replaces `DefaultNotificationPreferenceProvider` for resolving per-user notification delivery preferences (delivery mode and frequency). It calls the Tenant Service `GET /api/preferences` endpoint to retrieve user settings and caches results for 5 minutes per user to reduce cross-service calls.

### gRPC Services

**WalletNotificationGrpcService** — Receives inbound transaction notifications from the Register Service.

| RPC Method | Description |
|------------|-------------|
| `GetAllLocalAddresses` | Returns all locally-registered wallet addresses for bloom filter population |
| `NotifyInboundTransaction` | Notify a single inbound transaction for a local address |
| `NotifyInboundTransactionBatch` | Batch notify for recovery processing (multiple transactions) |

---

## Wallet Recovery (Feature 060)

The Wallet Service supports key recovery through passkey-based and organization-delegated recovery paths, enabling users to regain access to wallet signing keys without requiring mnemonic backup.

### Recovery Entities

- **RecoveryKeyWrap**: Stores an AES-256-GCM recovery key wrapped with an asymmetric public key (passkey or org recovery key). Linked to a wallet via `WalletId`.
- **RecoveryAuditLog**: Immutable audit trail of all recovery operations (wrap, unwrap, revocation).
- **RecoveryPathType** (enum): `Passkey`, `Organization` — identifies the recovery mechanism used.
- **Wallet extensions**: `EncryptedMasterKeyBlob` (encrypted master key for recovery) and `RecoveryEnabled` flag.

### Recovery Key Generation Flow

When a wallet is created via `WalletManager.CreateWalletAsync`, a recovery key is automatically generated:

1. An AES-256-GCM recovery key is generated by `IRecoveryKeyService`
2. The wallet's master key is encrypted with this recovery key and stored as `EncryptedMasterKeyBlob`
3. The recovery key is wrapped with the user's passkey public key (retrieved from Tenant Service via `PasskeyServiceClient`)
4. The wrapped key is stored as a `RecoveryKeyWrap` record
5. `RecoveryEnabled` is set to `true` on the wallet

### Recovery Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/wallets/{address}/recover/passkey` | Recover wallet using passkey-wrapped recovery key |
| POST | `/api/v1/wallets/{address}/recover/org` | Recover wallet using organization recovery delegation |
| POST | `/api/v1/wallets/{address}/recover/delegations/preserve` | Preserve existing delegations during recovery |
| GET | `/api/v1/wallets/{address}/recovery-status` | Get wallet recovery status and available recovery paths |

### Recovery Services

- **IRecoveryKeyService / RecoveryKeyService**: Core recovery key operations — AES-256-GCM key generation, asymmetric wrap/unwrap.
- **PasskeyRecoveryService**: Handles passkey-based recovery flow, retrieves passkey public keys from Tenant Service.
- **OrgRecoveryService**: Handles organization-delegated recovery with delegation revocation support.
- **PasskeyServiceClient**: HTTP client for retrieving passkey public keys from the Tenant Service.

### Tests

28 unit tests covering recovery key generation, wrap/unwrap operations, passkey and org recovery flows, and delegation preservation.

---

## File Download (Feature 085)

Allows wallet holders to download decrypted file attachments stored in blueprint action transactions.

### Endpoint

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/v1/wallets/{address}/files/download` | Download a decrypted file attachment |

### Query Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `registerId` | Yes | — | Register containing the source transaction |
| `actionTxId` | Yes | — | Transaction ID of the action holding the file |
| `fieldName` | Yes | — | JSON field name of the file attachment |
| `fileIndex` | No | `0` | Index within a multi-file field |

### Auth

JWT Bearer required. The calling wallet must be the owner or hold a delegated access grant with at least `ReadOnly` permission for the encrypted field.

### Response

`200 OK` — binary file stream. `Content-Type` and `Content-Disposition` headers reflect the original file MIME type and name.

---

## Resources

- **Specification**: [.specify/specs/sorcha-wallet-service.md](https://github.com/Sorcha-Platform/Sorcha/blob/master/.specify/specs/sorcha-wallet-service.md)
- **API Reference**: [Scalar UI](https://localhost:7084/scalar)
- **Development Status**: [docs/development-status.md](../../../docs/reference/development-status.md)
- **Architecture**: [docs/architecture.md](../../../docs/architecture.md)
- **BIP39 Standard**: [Bitcoin BIPs](https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki)
- **BIP44 Standard**: [Bitcoin BIPs](https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki)
- **OpenAPI Spec**: `https://localhost:7084/openapi/v1.json`

---

## License

Apache License 2.0 - See [LICENSE](https://github.com/Sorcha-Platform/Sorcha/blob/master/LICENSE) for details.

---

**Last Updated**: 2026-07-21
**Maintained By**: Sorcha Contributors
**Status**: 98% Complete
