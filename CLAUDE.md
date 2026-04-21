# Sorcha

A decentralised register platform for secure, multi-participant data flow orchestration built on .NET 10 and .NET Aspire.

Sorcha implements the **DAD** (Disclosure, Alteration, Destruction) security model - creating cryptographically secured registers where disclosure is managed through defined schemas, alteration is recorded on immutable ledgers, and destruction risk is eliminated through peer network replication.

**Current Status:** 100% MVD Complete | Production Readiness: 30%

---

## Quick Start

```bash
# Prerequisites: .NET 10 SDK, Docker Desktop

# Start all services (recommended)
docker-compose up -d

# Access points:
# - API Gateway:      http://localhost:80
# - Main UI:          http://localhost/app
# - Aspire Dashboard: http://localhost:18888

# CLI tool (after build):
# dotnet run --project src/Apps/Sorcha.Cli -- --help

# Alternative: Run with Aspire (debugging with breakpoints)
dotnet run --project src/Apps/Sorcha.AppHost
# Services available on HTTPS ports (7000-7290)

# Build and test
dotnet restore && dotnet build && dotnet test
```

---

## Tech Stack

| Layer | Technology | Purpose |
|-------|------------|---------|
| Runtime | .NET 10 / C# 13 | LTS runtime with latest features |
| Orchestration | .NET Aspire 13+ | Service discovery, health checks, telemetry |
| API | Minimal APIs + Scalar | REST endpoints with OpenAPI docs |
| Real-time | SignalR + Redis | WebSocket notifications |
| Databases | PostgreSQL / MongoDB / Redis | Relational, document, cache |
| Auth | JWT Bearer | Service-to-service and user authentication |
| Crypto | NBitcoin + Sorcha.Cryptography | HD wallets (BIP32/39/44), ED25519, P-256, RSA-4096 |
| Testing | xUnit + FluentAssertions + Moq | 1,200+ tests across 30 projects |

---

## Architecture

```
┌─────────────┐     ┌─────────────────┐     ┌──────────────────┐
│  Sorcha UI  │────▶│   API Gateway   │────▶│  Blueprint Svc   │
│  (Blazor)   │     │    (YARP)       │     │  (Workflows)     │
└─────────────┘     └─────────────────┘     └────────┬─────────┘
                            │                         │
                    ┌───────┴───────┐        ┌───────┴────────┐
              ┌─────▼─────┐   ┌─────▼─────┐  │  ┌────────────▼┐
              │  Wallet   │   │ Register  │◀─┘  │  Validator  │
              │  Service  │   │  Service  │     │   Service   │
              └─────┬─────┘   └─────┬─────┘     └─────────────┘
              │PostgreSQL │   │  MongoDB  │     │   Redis     │
```

**Key Services:**
| Service | Status | Port (Docker/Aspire) | Purpose |
|---------|--------|---------------------|---------|
| Blueprint | 100% | 5000 / 7000 | Workflow management, SignalR |
| Register | 100% | 5290 / 7290 | Distributed ledger, OData |
| Wallet | 98% | internal / 7001 | Crypto operations, HD wallets |
| Tenant | 98% | 5110 / 7110 | Multi-tenant auth, JWT issuer, Participant Identity, Platform Identity, Register Invitations |
| Validator | 95% | internal / 7004 | Consensus, chain integrity |
| Peer | 70% | 5002 / 7002 | P2P network, gRPC |
| API Gateway | 95% | 80 / 7082 | YARP reverse proxy |

---

## Participant Identity API

The Participant Identity Registry bridges Tenant Service users with Blueprint workflow participants and their Wallet signing keys.

### Endpoints (via API Gateway /api/*)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/organizations/{orgId}/participants` | Register participant (admin) |
| GET | `/organizations/{orgId}/participants` | List org participants |
| GET | `/organizations/{orgId}/participants/{id}` | Get participant details |
| PUT | `/organizations/{orgId}/participants/{id}` | Update participant |
| DELETE | `/organizations/{orgId}/participants/{id}` | Deactivate participant |
| POST | `/participants/search` | Search across accessible orgs |
| GET | `/participants/by-wallet/{address}` | Lookup by wallet address |
| POST | `/participants/{id}/wallet-links` | Initiate wallet link challenge |
| POST | `/participants/{id}/wallet-links/{challengeId}/verify` | Verify wallet signature |
| GET | `/participants/{id}/wallet-links` | List linked wallet addresses |
| DELETE | `/participants/{id}/wallet-links/{linkId}` | Revoke wallet link |
| POST | `/me/register-participant` | Self-register as participant |
| GET | `/me/participant-profiles` | Get all user's participant profiles |

### On-Register Participant Publishing (Tenant Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/organizations/{orgId}/participants/publish` | Publish participant record to register |
| PUT | `/organizations/{orgId}/participants/publish/{participantId}` | Update published participant record |
| DELETE | `/organizations/{orgId}/participants/publish/{participantId}` | Revoke published participant record |

### Published Participant Queries (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/registers/{registerId}/participants` | List published participants (status filter, pagination) |
| GET | `/registers/{registerId}/participants/by-address/{walletAddress}` | Get participant by wallet address |
| GET | `/registers/{registerId}/participants/{participantId}` | Get participant by ID |
| GET | `/registers/{registerId}/participants/by-address/{walletAddress}/public-key` | Resolve public key for encryption |

### Key Models

- **ParticipantIdentity**: User + Organization + Status + DisplayName
- **LinkedWalletAddress**: WalletAddress + VerifiedAt + Status (max 10 per participant)
- **WalletLinkChallenge**: Nonce + Expiration (5 min) for signature verification
- **PublishedParticipantRecord**: On-register identity with addresses, version, status
- **PublicKeyResolution**: Resolved public key for field-level encryption (410 Gone if revoked)

### Service Client

```csharp
// Use IParticipantServiceClient from Sorcha.ServiceClients
var participant = await participantClient.GetByIdAsync(orgId, participantId);
var canSign = await participantClient.ValidateSigningCapabilityAsync(orgId, participantId);
```

---

## Register Invitation API

Private register invitation system using cryptographic envelopes (ED25519 sign + X25519 encrypt via Wallet Service). Register owners invite organizations by DID; target orgs accept by decrypting and verifying the token.

### Endpoints (via API Gateway /api/*)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/organizations/{orgId}/register-invitations` | Create signed+encrypted invitation (admin) |
| POST | `/organizations/{orgId}/register-invitations/accept` | Accept invitation token (admin) |
| GET | `/organizations/{orgId}/register-invitations` | List invitations (filter: sent/received/all) |
| DELETE | `/organizations/{orgId}/register-invitations/{invitationId}` | Revoke pending invitation (admin) |

### Key Models

- **RegisterInvitationRecord**: SourceOrgId + TargetOrgDid + RegisterId + Nonce + Status (Pending/Accepted/Revoked/Expired) + ExpiresAt
- **InvitationNonce**: Replay protection via unique DB index on consumed nonces
- **InvitationTokenEnvelope**: Version + ED25519 Signature + X25519 EncryptedPayload + SenderDID
- **InvitationPayload**: RegisterId + SourceOrgDid + TargetOrgDid + Nonce + ExpiresAt + Names
- **SorchaDidIdentifier.Organization**: `did:sorcha:org:{walletAddress}` — new DID type for org identity

### Crypto Flow

1. **Create**: Serialize payload → encrypt to target wallet (X25519) → sign encrypted blob (ED25519) → base64 envelope token
2. **Accept**: Decode token → verify sender signature → decrypt with target wallet → validate nonce/expiry/target → create `SubscriptionType.Invited` subscription

---

## Trust Hardening API (Feature 079)

Transaction receipts, Merkle inclusion proofs, revocation transactions, and offline verification bundles. All operate on transaction envelopes (FLE-compatible).

### Transaction Receipts & Proofs (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/registers/{registerId}/receipts/batch` | Store receipt batch (internal) |
| GET | `/registers/{registerId}/transactions/{txId}/receipt` | Get receipt by tx ID |
| GET | `/registers/{registerId}/dockets/{docketNumber}/receipts` | List docket receipts |
| POST | `/registers/{registerId}/receipts/verify` | Verify receipt (public) |
| GET | `/registers/{registerId}/transactions/{txId}/inclusion-proof` | Generate Merkle proof |
| POST | `/registers/{registerId}/inclusion-proofs/verify` | Verify proof (public) |

### Revocation & Status (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/registers/{registerId}/transactions/revoke` | Submit revocation |
| GET | `/registers/{registerId}/transactions/{txId}/status` | Get lifecycle status (active/revoked/superseded) |

### Verification Bundles (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/registers/{registerId}/transactions/{txId}/verification-bundle` | Export portable bundle |
| POST | `/registers/{registerId}/verification-bundles/verify` | Verify bundle (public) |

### Key Models

- **TransactionReceipt**: Signed attestation of finality with embedded Merkle inclusion proof and validator signature(s)
- **MerkleInclusionProof**: Compact proof (log2(n) steps) of transaction inclusion in a docket
- **RevocationPayload**: Revocation reason + target tx reference (Superseded/Erroneous/Compromised/Expired/Withdrawn/Regulatory)
- **VerificationBundle**: Portable package (VC + receipt + proof + revocation status) for offline verification
- **TransactionLifecycleStatus**: Active, Revoked, or Superseded

### Transaction Lifecycle Ticks (Wallet Service)

WhatsApp-style delivery indicators tracked per-wallet:
- Grey tick: Submitted (Pending)
- Blue tick: Sealed in docket (Confirmed)
- Double blue ticks: Receipt confirmed (Receipted)

`WalletTransaction` entity tracks both outbound (signed) and inbound (recipient) transactions.

---

## Stored Data Transactions API (Feature 085)

File attachments as first-class fields in blueprint action schemas. Files are transparently chunked (≤4MB), encrypted with HKDF-SHA256 derived per-chunk keys (XChaCha20-Poly1305), and submitted as staged transactions. The Wallet Service mediates file retrieval.

### File Chunk Submission (Blueprint Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/file-chunks` | Submit encrypted file chunk (staged, pre-action) |

### File Download (Wallet Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/v1/wallets/{address}/files/download` | Fetch, decrypt, reassemble, stream file |

Query params: `registerId`, `actionTxId`, `fieldName`, `fileIndex` (default 0)

### Blueprint Schema Extension

File fields use `format: "file-reference"` with `x-file` extension:
```json
{
  "sitePhoto": {
    "type": "string",
    "format": "file-reference",
    "x-file": { "accept": ["image/jpeg"], "maxSizePerFile": "16MB", "maxChunks": 10 }
  }
}
```

### Key Models

- **FileReference**: Runtime value in action payload (fileName, contentType, size, hash, salt, chunkTransactionIds, masterKeyId)
- **FileChunkMetadata**: Per-chunk transaction metadata (type="file-chunk", chunkIndex, totalChunks, fileHash)
- **FileSchemaExtension**: Blueprint schema x-file extension (accept, maxSizePerFile, maxChunks)
- **Limits**: 4MB chunks, 10 max per file, 40MB ceiling, 30-min orphan timeout

### Encryption Flow

1. Server generates random `MasterFileKey` + `salt` per file upload session
2. Each chunk encrypted with `HKDF-SHA256(MasterFileKey, salt, "sorcha-chunk-{n}")` → XChaCha20-Poly1305
3. `MasterFileKey` wrapped per recipient in action payload Challenges
4. Download: Wallet Service unwraps key, derives chunk keys, decrypts, reassembles, verifies SHA-256

---

## Validator Key Roster (Feature 086)

Register genesis control records include a `validators` field declaring authorized docket signing keys. Remote peers extract these keys to verify synced dockets.

### Key Design Points
- **Signing key**: Purpose-derived from system wallet using `"sorcha:docket-signing"` derivation context (distinct from `"sorcha:register-control"` used for genesis transactions)
- **DocketBuilder**: Signs with `SignTransactionAsync(walletAddress, hash, "sorcha:docket-signing", isPreHashed: true)` — NOT the root wallet key
- **ValidatorRoster**: List of `ValidatorRosterEntry` (1-10 entries) + `RequiredSignatures` (default 1) + `Version`
- **ValidatorKeyCache**: Multi-key roster per register; `IsAuthorizedSigner(registerId, publicKey)` checks Active + Rotated keys
- **Governance**: `AddValidator`, `RemoveValidator`, `RotateValidatorKey` operation types on the existing governance proposal endpoint
- **External roster (FR-014)**: Register creation accepts optional external validator list for future System Register (087)

---

## Org Key Derivation API (Feature 083)

Organisation-level HD key derivation using Sorcha-specific BIP32 paths (`m/0x534F52'/org'/dept'/user'/usage/index`). Custodial mode with pluggable seed protection.

### Endpoints (Wallet Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/wallets/org/{orgId}/master-key` | Provision org master key (one-shot, returns mnemonic once) |
| POST | `/api/wallets/org/{orgId}/derive-key` | Derive user key (idempotent) |
| POST | `/api/wallets/org/{orgId}/keys/{derivedKeyId}/rotate` | Rotate key (new at next index, old decrypt-only) |
| DELETE | `/api/wallets/org/{orgId}/keys/{derivedKeyId}` | Revoke key (wallet locked, DID event for identity keys) |

### Key Models

- **OrgMasterKey**: Organisation root seed, encrypted at rest, one per org
- **DerivedKeyRecord**: User key derived from org master, tracks path/usage/index/status
- **KeyUsage**: Identity (0), VCIssuance (1), Governance (2), Communications (3), ServiceAuth (4)
- **CustodyMode**: Custodial (implemented), CoSigned (schema only), SelfCustody (schema only)

---

## Platform Organisation Topology API

Three-tier org topology: system admin org, public org (social login + email/password), and private orgs. `PlatformUser` is the cross-org identity anchor; `UserIdentity` handles per-org authorisation.

### Well-Known Organisation IDs

| ID | Purpose |
|----|---------|
| `00000000-0000-0000-0000-000000000001` | System Admin Org |
| `00000000-0000-0000-0000-000000000002` | Public Org |

### Platform Management Endpoints (SystemAdmin only)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/platform/organizations` | List all orgs (paginated, status filter) |
| PUT | `/platform/organizations/{orgId}/status` | Update org status (Active/Suspended) |
| GET | `/platform/organizations/{orgId}/users` | Audit org users (read-only) |
| POST | `/platform/organizations` | Create org with admin invite |
| GET | `/platform/settings` | Get platform settings |
| PUT | `/platform/settings/public-org` | Enable/disable public org |

### Authentication & Org Switching Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/auth/social/initiate` | Start social login OAuth flow |
| POST | `/auth/social/callback` | Complete social login callback |
| POST | `/auth/register` | Email/password signup (public org) |
| GET | `/auth/me/organizations` | List user's org memberships |
| POST | `/auth/switch-org` | Switch active org (re-issues JWT) |

### Platform Identity Models

- **PlatformUser**: Cross-org identity with email uniqueness, social logins, passkey credentials
- **PlatformSocialLogin**: OAuth provider links (Google, GitHub, Microsoft, Apple)
- **PlatformUserOrgMembership**: Maps platform users to org-scoped roles
- **PlatformSettings**: Platform governance (public org enable/disable, max orgs per user)

---

## Consumer Persona API (Feature 092)

Per-user identity persona stored as ciphertext in Tenant Service with the content
key derived by Wallet Service under `sorcha:persona-vault`. Read side returns
attributes wrapped in `PersonaAttribute<T>` carrying provenance. `SorchaFormRenderer`
consumes the persona to autofill recognised form fields with a cream tint and a
visible `self` provenance tick. Edit releases the claim. A global toggle switches
silent apply to a one-click "Fill from profile" button.

### Endpoints (via API Gateway /api/*)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/me/persona` | Read the signed-in user's persona (returns empty for new users, never 404) |
| PUT | `/me/persona` | Replace the persona with a full `PersonaAttributesV1` payload |
| DELETE | `/me/persona` | Delete the persona row (idempotent) |

Internal (not routed through gateway):

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/v1/wallets/{address}/persona/encrypt` | Derive content key, encrypt payload |
| POST | `/api/v1/wallets/{address}/persona/decrypt` | Derive content key, decrypt ciphertext |

### Key Models

- **PersonaAttributesV1**: Plaintext write-side shape — givenName, familyName, fullName, dateOfBirth, emails, phones, addresses, nationalities. Each multi-value list capped at 5 with exactly one default.
- **PersonaReadModelV1**: Wire shape with `Default*` + `All*` pairs for multi-value attributes, each wrapped in `PersonaAttribute<T>`.
- **PersonaAttribute<T>**: `Value`, `Source` (`SelfAsserted`/`VerifiedCredential`), `VerifiedBy` (issuer DID, always null in v1), `LastUpdated`.
- **PlatformUserPersona**: EF entity — 1:1 with `PlatformUser`, hard-delete cascade, XChaCha20-Poly1305 ciphertext with 24-byte nonce and `wrappedKeyRef == walletAddress`.
- **PersonaFillResult**: Per-field autofill decision carried by the resolver (field path, attribute name, value, source, `PersonaMatchMode`).

### Schema Extension

Form authors can pin a field to a specific persona attribute via a JSON-Schema extension:

```json
{
  "applicantEmail": { "type": "string", "format": "email", "x-persona": "defaultEmail" },
  "nextOfKinEmail": { "type": "string", "format": "email", "x-persona": false }
}
```

Without an explicit tag, the conservative inference allowlist applies: `format: "email"` → default email, `format: "tel"` → default phone, field names `dateOfBirth`/`dob`/`birthDate` → date of birth, postal-address object shape → default address.

### Cryptography

- **Derivation purpose**: `sorcha:persona-vault` (BIP44-style index 104 under the `SorchaDerivationPaths` constants).
- **AEAD**: XChaCha20-Poly1305 via the existing `ISymmetricCrypto`, 24-byte nonce.
- **HKDF**: Per-file chunk keys derived with HKDF-SHA256 in `PersonaCryptoService`.
- **Ciphertext location**: Tenant DB only. Content key never leaves Wallet Service. Reading requires a service token carrying `RequirePersonaCrypto` policy.

### Client surface

```csharp
// Sorcha.UI.Core.Services.Persona.IPersonaService — session-cached client facade
var persona = await personaService.GetAsync();
await personaService.UpdateAsync(newAttributes);
await personaService.SetAutofillEnabledAsync(false);
```

`SorchaFormRenderer` resolves fills via `PersonaAutofillResolver` and renders a disclosure summary banner (`PersonaFillSummary`) above the form with Review and Clear all actions. When the global autofill toggle is off, the same banner renders a one-click "Fill from profile" button instead. See `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyProfile.razor` for the user-facing page.

---

## System Register Genesis Trust Anchor (Feature 099)

The system register is bootstrapped from a pre-signed genesis block produced by an offline ceremony. Instances never create the genesis at runtime — they consume it from a genesis file (config path or embedded resource).

### Genesis Ceremony CLI

```bash
# Create a new network genesis (offline — no services needed)
sorcha system-register create --network-id sorcha-dev

# Outputs:
#   system-register-genesis.json  → embed in source tree or deploy as config
#   genesis-validator-key.json    → import into first validator, then destroy

# Verify a genesis file
sorcha system-register verify path/to/system-register-genesis.json

# Import validator key into running Wallet Service (first validator only)
sorcha system-register import-validator-key --key genesis-validator-key.json
```

### Bootstrap Flow (Register Service)

1. **Check local** — system register exists? Proceed normally.
2. **Try peer sync** — sync from peers, verify genesis signature against trust anchor.
3. **Ingest genesis** — load pre-signed genesis file, submit to Validator Service.
4. **Stop** — if no genesis file and no peers, log actionable message and halt.

### Configuration

```json
{
  "SystemRegister": {
    "GenesisFile": "/etc/sorcha/system-register-genesis.json"
  }
}
```

When `GenesisFile` is null, the embedded resource in `Sorcha.Register.Models` is used.

### Key Files

| File | Purpose |
|------|---------|
| `src/Common/Sorcha.Register.Models/Genesis/` | Genesis file models and loader |
| `src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json` | Embedded dev genesis |
| `src/Services/Sorcha.Register.Service/Services/GenesisIngestionService.cs` | Load, verify, submit genesis |
| `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs` | 4-step bootstrap flow |
| `src/Services/Sorcha.Peer.Service/Replication/SystemRegisterSyncVerifier.cs` | Peer genesis trust check |
| `src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs` | CLI ceremony commands |

---

## Project Structure

```
src/
├── Apps/
│   ├── Sorcha.AppHost/              # .NET Aspire orchestrator
│   ├── Sorcha.Admin/                # Blazor WASM admin UI (host)
│   │   └── Sorcha.Admin.Client/     # Admin UI client components
│   ├── Sorcha.Agent/                # Autonomous actor agent CLI
│   ├── Sorcha.Cli/                  # Administrative CLI tool
│   ├── Sorcha.Demo/                 # Demo application
│   ├── Sorcha.McpServer/            # MCP Server for AI assistants (Claude Desktop, etc.)
│   └── Sorcha.UI/                   # Main UI application
│       ├── Sorcha.UI.Core/          # Shared UI components
│       ├── Sorcha.UI.Web/           # Web host
│       └── Sorcha.UI.Web.Client/    # Web client (Blazor WASM)
├── Common/
│   ├── Sorcha.Blueprint.Models/     # Domain models with JSON-LD
│   ├── Sorcha.Cryptography/         # Multi-algorithm crypto (ED25519, P-256, RSA)
│   ├── Sorcha.Register.Models/      # Register domain models
│   ├── Sorcha.ServiceClients/       # Consolidated HTTP/gRPC clients
│   ├── Sorcha.ServiceClients.Http/  # HTTP REST clients + SignalR (NuGet, mobile-friendly)
│   ├── Sorcha.ServiceDefaults/      # Aspire shared configuration
│   ├── Sorcha.Storage.*/            # Storage abstraction layer (5 projects)
│   │   ├── Abstractions/            # IRepository<T>, IUnitOfWork interfaces
│   │   ├── EFCore/                  # Entity Framework Core implementation
│   │   ├── InMemory/                # In-memory implementation (testing)
│   │   ├── MongoDB/                 # MongoDB implementation
│   │   └── Redis/                   # Redis caching implementation
│   ├── Sorcha.Tenant.Models/        # Tenant domain models
│   ├── Sorcha.TransactionHandler/   # Transaction building/serialization
│   ├── Sorcha.Validator.Core/       # Enclave-safe validation library
│   ├── Sorcha.Wallet.Core/          # Wallet domain logic
│   └── Sorcha.Wallet.Portable/      # Portable wallet: entities, enums, derivation (NuGet)
├── Core/
│   ├── Sorcha.Blueprint.Engine/     # Portable execution (WASM-compatible)
│   ├── Sorcha.Blueprint.Fluent/     # Fluent API for blueprint construction
│   ├── Sorcha.Blueprint.Schemas/    # Schema management with caching
│   ├── Sorcha.Register.Core/        # Ledger business logic
│   └── Sorcha.Register.Storage.*/   # Register-specific storage (3 projects)
│       ├── Sorcha.Register.Storage/ # Storage abstractions
│       ├── InMemory/                # In-memory implementation
│       └── MongoDB/                 # MongoDB implementation
└── Services/                        # 7 microservices
    ├── Sorcha.ApiGateway/           # YARP reverse proxy
    ├── Sorcha.Blueprint.Service/    # Workflow management
    ├── Sorcha.Peer.Service/         # P2P networking (gRPC)
    ├── Sorcha.Register.Service/     # Distributed ledger
    ├── Sorcha.Tenant.Service/       # Multi-tenant authentication
    ├── Sorcha.Validator.Service/    # Blockchain validation
    └── Sorcha.Wallet.Service/       # Crypto wallet management

tests/                               # 30 test projects
├── *.Tests/                         # Unit tests per component
├── *.IntegrationTests/              # Integration tests
├── *.PerformanceTests/              # Performance/load tests
└── Sorcha.UI.E2E.Tests/             # End-to-end Playwright tests
```

**Project Count:** 42 source projects, 31 test projects

---

## Development Guidelines

### File Naming
- **C# Files:** PascalCase (e.g., `WalletManager.cs`, `IActionStore.cs`)
- **Test Files:** `{ClassName}Tests.cs` (e.g., `WalletManagerTests.cs`)

### Code Naming
| Element | Convention | Example |
|---------|------------|---------|
| Classes/Interfaces | PascalCase, `I` prefix for interfaces | `WalletManager`, `IWalletService` |
| Methods/Properties | PascalCase | `CreateWalletAsync`, `IsEnabled` |
| Parameters/Variables | camelCase | `walletId`, `transactionData` |
| Private fields | _camelCase | `_repository`, `_logger` |
| Constants | PascalCase | `MaxRetryCount`, `DefaultTimeout` |
| Async methods | `Async` suffix | `ValidateAsync`, `ProcessAsync` |

### Test Naming
```csharp
// Pattern: MethodName_Scenario_ExpectedBehavior
public async Task ValidateAsync_ValidData_ReturnsValid() { }
public void Build_WithoutTitle_ThrowsInvalidOperationException() { }
```

### Import Order
```csharp
using System.Text.Json;           // 1. System
using Microsoft.Extensions.DI;    // 2. Microsoft
using FluentAssertions;           // 3. Third-party
using Sorcha.Blueprint.Models;    // 4. Sorcha
```

### Service Folder Structure
```
Services/Sorcha.*.Service/
├── Endpoints/           # Minimal API endpoint definitions
├── Extensions/          # Service collection extensions
├── GrpcServices/        # gRPC service implementations (if applicable)
├── Mappers/             # DTO/Model mapping
├── Models/              # Request/Response DTOs
├── Services/            # Business logic
│   ├── Interfaces/      # IWalletService, IKeyManagementService
│   └── Implementation/  # WalletManager, KeyManagementService
└── Program.cs           # Entry point
```

---

## Critical Patterns

### 1. Use Scalar for OpenAPI (NOT Swagger)
```csharp
// .NET 10 built-in OpenAPI with Scalar UI
app.MapPost("/api/wallets", handler)
    .WithName("CreateWallet")
    .WithSummary("Create a new wallet");
```

### 2. Use Consolidated Service Clients
```csharp
// Always use Sorcha.ServiceClients - NEVER create duplicate clients
builder.Services.AddServiceClients(builder.Configuration);
```

### 3. Blueprint Creation Policy
- **Primary:** Create blueprints as JSON or YAML files
- **Secondary:** Fluent API for programmatic/dynamic blueprint generation
```json
{ "title": "...", "participants": [...], "actions": [...] }
```

### 4. JsonSchema.Net Requires JsonElement
```csharp
// CRITICAL: Evaluate() expects JsonElement, not JsonNode
JsonElement element = JsonSerializer.Deserialize<JsonElement>(json);
var result = schema.Evaluate(element);
```

### 5. Storage Abstraction Pattern
```csharp
// Use IRepository<T> from Sorcha.Storage.Abstractions
public class WalletService
{
    private readonly IRepository<Wallet> _repository;
    public WalletService(IRepository<Wallet> repository) => _repository = repository;
}
```

### 6. Instance Reference Configuration
Blueprints should define an `instanceReference` to generate human-readable identifiers for workflow instances (e.g., "CP-RIV-14-A7K3"). The reference is auto-generated from first-action payload fields and stored as public metadata on the instance.
```json
"instanceReference": {
  "prefix": "CP",
  "components": [
    { "field": "/projectName", "transform": "FirstWord", "chars": 3 },
    { "field": "/siteAddress", "transform": "FirstWord", "chars": 3 }
  ]
}
```
- **prefix**: 1-5 uppercase alpha chars identifying the workflow type
- **components**: 1-5 field extractions from the starting action's schema
- **transforms**: `FirstWord` (split on space, take first), `Truncate` (take first N chars). All output is uppercased.
- A 4-char uniqueness hash is auto-appended
- The reference is **public metadata** — field values referenced here will be visible in plaintext

### 7. License Header (Required)
```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
```

### 8. Centralised Rate Limiting (SEC-002)
All services use `builder.AddRateLimiting()` from ServiceDefaults. Limits are driven by `RateLimitSettings` bound from `"RateLimiting"` in `appsettings.json`. **Do NOT add custom `AddRateLimiter` calls in individual services.**

```csharp
// All services — registers all standard policies
builder.AddRateLimiting();

// Endpoints reference shared policy names
.RequireRateLimiting(RateLimitPolicies.Api)           // default
.RequireRateLimiting(RateLimitPolicies.PlatformAuth)   // login/register
.RequireRateLimiting(RateLimitPolicies.TotpValidation) // 2FA
.RequireRateLimiting(RateLimitPolicies.Strict)         // wallet ops
```

Default values are very relaxed (100k/min) for pre-release development. Tighten in `appsettings.Production.json`. Inject `IOptions<RateLimitSettings>` for non-HTTP rate limiting (e.g. wallet notifications, MCP server).

### 9. Open Participants & Late Binding

Citizen-facing services accept a walk-in public user as the applicant without requiring a pre-existing participant record. The contract lives in three places and must be honoured end-to-end:

1. `Action.IsStartingAction = true` is the **open** flag. Any authenticated wallet may submit; the first qualifying submitter is late-bound to the action's `Sender` participant for the life of the instance. Re-binding is immutable — a second submission from a different wallet throws.
2. The participant referenced by `Action.Sender` on a starting action MUST have `Participant.WalletAddress = null` in the published blueprint. Pre-baking a wallet is the foot-gun the publish-time guardrail **`VAL_BP_010`** exists to catch.
3. Walkthrough authors MUST NOT include open participants in their `$walletMap`. The correct shape is to omit the citizen/applicant entry entirely and let the runtime late-bind.

```powershell
# CORRECT shape for citizen-facing walkthroughs:
$walletMap = @{
    "government-assessor" = $assessorWallet.Address
    # "citizen" is intentionally absent — late-bound at runtime
}
```

Credential-bootstrapped flows (e.g. Driving Licence requiring a Verified Citizen credential) layer `credentialRequirements` on the open starting action — the HAIP presentation gate fires *before* the late-bind block, so only credential holders become the bound applicant.

Runtime source: `ValidationEngine.cs:1027` (validator skips strict wallet check for starting actions), `ActionExecutionService.cs:196-216` (strict check fires only when `WalletAddress` non-null), `ActionExecutionService.cs:309-332` (late-bind block, persisted via `IInstanceStore.UpdateAsync`). Authoritative documentation: `.claude/skills/blueprint-builder/SKILL.md` → "Open Participants & Late Binding" section. Feature design: `specs/103-verified-citizen-v2/`.

### 10. Review Summary (`x-review`) — Feature 107

Mark a wizard page as a read-only summary of the form's prior pages. The renderer draws a stylised credential id-card previewing what the citizen will receive once issued. The same component renders the assessor's pending-review screen and the issued credential's wallet detail view — one component, three states, with the watermark (`Draft` / `Pending` / `Issued` / `None`) derived from the hosting action's runtime state.

```jsonc
{
  "title": "Review your details",
  "x-review": {
    "layout": "id-card",                  // v1 only; passport-page / tabular / receipt reserved
    "editable": true,                     // Generates Edit-X per section
    "header": {
      "issuerName": "Government of Scotland",
      "credentialName": "Assured Identity",
      "colourTheme": "identity-navy"     // v1: identity-navy | licence-pink
    }
  }
}
```

**Stacked-cards variant** fires automatically when the hosting action declares both `credentialRequirements` and `credentialIssuanceConfig` — the renderer draws two id-cards on the review page (presented identity above with a ✓ Verified chip, credential-to-be below with a Pending watermark).

**Portrait capture** rides the same renderer via two extensions on `x-file`: `capture: "user"` requests the front-facing camera on mobile; `embedAs: "image-token-jpeg-240x320"` triggers the client-side resizer, producing a base64 JPEG token at `{fieldPointer}/tokenImageBase64` alongside the chunked full-resolution original. Server-side gate in `ActionExecutionService.BuildClaimsFromMappings` enforces ≤27KB base64; oversize → claim omitted with `WARN_CRED_PORTRAIT_OVERSIZE_001`, credential still issues.

Runtime source: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/ReviewSummaryRenderer.razor`, `IdCardLayout.razor`, `SchemaLayoutParser.cs`. Authoritative documentation: `.claude/skills/blueprint-builder/SKILL.md` → `x-review` + `x-file.capture`/`embedAs` sections. Contract: `specs/107-assured-identity-v1/contracts/x-review-extension.md`.

### 11. Ownership-agnostic submission + derived relationship — Feature 108

Register.Service is the authoritative source of per-register state on each installation. `RegisterLocalRelationship` is derived on demand from the latest control record (genesis + governance control transactions) plus the node's local wallet/validator key — not stored as a flag. `Register.SyncState` is a typed enum (`Indeterminate` / `Syncing` / `CaughtUp` / `Error`) derived from local docket height, peer-advert high-water-mark, and validator sealing progress.

**Submission rule (Blueprint.Service):** `ActionExecutionService` makes two concurrent calls on every submission — `IValidatorServiceClient.SubmitTransactionAsync` (local mempool) and `IPeerServiceClient.DistributeTransactionAsync` (peer fan-out to source peers via the `TransactionDistribution.SubmitTransaction` gRPC RPC). No ownership-aware branching — each downstream service uses its derived relationship to decide what to do. Validator seals iff on the roster; peer fan-out reaches the owner when local is a subscriber.

**Validator enrolment (Validator.Service):** `IRegisterMonitoringRegistry` is populated by `RegisterMonitoringBootstrap` at startup + on `register:relationship-changed` Redis events + every 5-minute safety poll. The previous side-effect enrolment from `/api/v1/transactions/validate` is removed — subscribers never attempt to seal, eliminating chain-fork risk.

**Observation intake (Register.Service):** Peer.Service pushes `PeerHeightObservation` on every advert ingest; Validator.Service pushes `ValidatorSealingObservation` on docket seal. Neither is persisted — they feed the in-memory `IObservationStore` that `RegisterSyncStateResolver` consumes.

Endpoints: `GET /api/registers/{id}/local-relationship`, `GET /api/registers/{id}/sync-state`, `GET /api/internal/my-validated-registers` (requires `X-Validator-Public-Key` header). Internal intake: `POST /api/internal/registers/{id}/peer-height-observation`, `POST /api/internal/registers/{id}/validator-observation`, `POST /api/internal/peer/distribute/{id}`.

Runtime source: `src/Core/Sorcha.Register.Core/LocalRelationship/`, `src/Core/Sorcha.Register.Core/SyncState/`, `src/Core/Sorcha.Register.Core/Observations/`, `src/Services/Sorcha.Validator.Service/Services/RegisterMonitoringBootstrap.cs`, `src/Services/Sorcha.Peer.Service/GrpcServices/TransactionDistributionGrpcService.cs` (SubmitTransaction RPC). Spec: `specs/108-register-local-relationship/`.

---

## Key Documentation

| Document | Purpose |
|----------|---------|
| `.specify/constitution.md` | Architectural principles (read first!) |
| `.specify/MASTER-TASKS.md` | Task tracking with priorities |
| `.specify/AI-CODE-DOCUMENTATION-POLICY.md` | MANDATORY documentation requirements |
| `docs/getting-started/PORT-CONFIGURATION.md` | Complete port assignments |
| `docs/guides/AUTHENTICATION-SETUP.md` | JWT configuration guide |
| `docs/reference/development-status.md` | Current completion status |
| `docs/reference/architecture.md` | System architecture diagrams |

---

## Context Management (Core Guideline)

**Problem:** Large files auto-loaded into every session waste context window. After compaction, sessions restart with 100KB+ of reference material that may not be relevant.

**Rules:**
1. **MASTER-TASKS.md** — Active work only. Completed phases archived to `MASTER-TASKS-ARCHIVE.md`. Read the archive on-demand when historical context is needed.
2. **MEMORY.md** — Cap at 50 lines. Active patterns and preferences only. No historical fix notes or completed branch details.
3. **Plan/spec files** — Read on-demand when implementing, not at session start. Large reference docs pollute compact summaries.
4. **Current work focus** — Store in MEMORY.md under `## Current Branch` with branch name, remaining tasks, and build status. Update on session end.
5. **On continue** — Check `MEMORY.md > Current Branch` section first. Only load the plan/task files if continuing that work.
6. **Settings permissions** — Use broad patterns (`Bash(*)`) not one-off approvals. Keep the list under 20 entries.

**On session end or before compact:**
- Update `MEMORY.md > Current Branch` with progress
- Do NOT read large reference files just to summarize them

---

## AI Assistant Requirements

### MANDATORY: Update these when generating code
1. `.specify/MASTER-TASKS.md` - Task status (📋 → 🚧 → ✅)
2. README files - If features/APIs changed
3. `docs/` files - If architecture/status changed
4. OpenAPI/XML docs - All endpoints documented

**PRs without documentation updates will NOT be approved.**

### Documentation Sync Policy

When modifying code, ensure corresponding documentation stays in sync:

- **Service README** — If you add/change endpoints, configuration, or features, update the service's README.md
- **docs/reference/API-DOCUMENTATION.md** — If you add/change REST or gRPC endpoints
- **docs/guides/AUTHENTICATION-SETUP.md** — If you change auth flows, policies, or token handling
- **docs/getting-started/PORT-CONFIGURATION.md** — If you add/change port assignments
- **docs/reference/development-status.md** — If you complete a feature or change service status
- **CLAUDE.md** — If you change architectural patterns or conventions
- **XML comments** — All public API methods must have `/// <summary>` to avoid build warnings
- **OpenAPI descriptions** — All Minimal API endpoints must have `.WithSummary()` and `.WithDescription()`

Documentation debt compounds quickly. A 2-minute doc update now prevents 30 minutes of confusion later.

### DO
- Read `.specify/constitution.md` before coding
- Check `.specify/MASTER-TASKS.md` for task priorities
- Write tests alongside code (>85% coverage)
- Use `Sorcha.ServiceClients` for HTTP calls
- Use `Sorcha.Cryptography` for crypto operations
- Use `Sorcha.Storage.*` for data persistence
- Reference task IDs in commits

### DON'T
- Use Swagger/Swashbuckle (use Scalar)
- Create duplicate service clients
- Use `JsonNode` with JsonSchema.Net (use `JsonElement`)
- Commit secrets or credentials
- Skip documentation updates
- Skip documentation updates when changing code (see Documentation Sync Policy above)
- Store mnemonics (user responsibility to backup)

---

## Commands

```bash
# Docker
docker-compose up -d                              # Start services
docker-compose logs -f <service>                  # View logs
docker-compose build <service> && docker-compose up -d --force-recreate <service>  # Rebuild

# MCP Server (for AI assistants)
docker-compose run mcp-server --jwt-token <token> # Run MCP server with JWT auth
# Or use environment variable:
# SORCHA_JWT_TOKEN=<token> docker-compose run mcp-server

# .NET Aspire
dotnet run --project src/Apps/Sorcha.AppHost      # Start with Aspire

# Build & Test
dotnet restore && dotnet build                    # Build solution
dotnet test                                       # Run all tests
dotnet test --filter "FullyQualifiedName~Blueprint"  # Filtered tests
dotnet test --collect:"XPlat Code Coverage"       # With coverage

# Code Quality
dotnet format                                     # Format code
```

---

## Claude Code Skills

| Command | Purpose |
|---------|---------|
| `/speckit.specify` | Create/update feature specification |
| `/speckit.plan` | Generate implementation plan |
| `/speckit.tasks` | Generate task list |
| `/speckit.implement` | Execute implementation |
| `/speckit.clarify` | Ask clarification questions |
| `/speckit.analyze` | Cross-artifact analysis |

---

## Walkthroughs

Interactive demos and test scripts are in `walkthroughs/`:

| Walkthrough | Status | Purpose |
|-------------|--------|---------|
| `AdminIntegration/` | ✅ | Blazor WASM behind API Gateway |
| `McpServerBasics/` | ✅ | MCP Server auth and tool verification |
| `RegisterCreationFlow/` | ✅ | Register lifecycle, CLI, OData |
| `WalletVerification/` | ✅ | Multi-algorithm crypto (ED25519/P-256/RSA) |
| `ConstructionPermit/` | ✅ | 4-org, 5-participant, encrypted workflows, routing, rejection, VCs |
| `SelfBuildHouse/` | ✅ | 6-org, 2-register, cross-register VCs, credential chains |
| `AssuredIdentity/` | ✅ | Feature 107 — canonical citizen identity (5-page wizard + id-card review + optional portrait) + driving licence credential chain + unattended assessor agents + cross-peer smoke |
| `DistributedRegister/` | ✅ | Cross-machine P2P replication |
| `PerformanceBenchmark/` | ✅ | TPS, latency, concurrency benchmarks |

See `walkthroughs/README.md` for full details and the shared module API.

---

## Branch & PR Policy

**All changes MUST go through branches and pull requests.** Direct pushes to `master` are blocked by GitHub branch protection.

```bash
# Standard workflow
git checkout -b feature/description    # Create branch
# ... make changes, commit ...
git push -u origin feature/description # Push branch
gh pr create --fill                    # Create PR
gh pr merge --squash                   # Merge after review
```

- Never commit directly to `master` — it will be rejected
- Use descriptive branch names: `feature/`, `fix/`, `docs/`, `chore/`
- PRs can be self-merged (0 approvals required for solo dev)
- Keep PRs focused — one logical change per PR

---

## Commit Format

```
feat: [TASK-ID] - Brief description

- Implementation details
- Documentation updated: README.md, MASTER-TASKS.md

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>
```

---

**Version:** 2.6 | **Updated:** 2026-03-16 | Built with .NET 10 and .NET Aspire


## Skill Usage Guide

When working on tasks involving these technologies, invoke the corresponding skill:

| Skill | Invoke When |
|-------|-------------|
| postgresql | Manages PostgreSQL databases and Entity Framework Core integration |
| scalar | Generates and configures Scalar OpenAPI UI for API documentation |
| redis | Implements Redis caching and session management |
| signalr | Implements real-time WebSocket communication using SignalR |
| minimal-apis | Defines REST endpoints using Minimal APIs with OpenAPI documentation |
| yarp | Configures YARP reverse proxy for API gateway routing |
| mongodb | Configures MongoDB document storage and query operations |
| aspire | Configures .NET Aspire orchestration, service discovery, and telemetry |
| dotnet | Manages .NET 10 runtime, C# 13 syntax, and project configuration |
| blazor | Builds Blazor WASM components for admin and main UI applications |
| fluent-assertions | Creates readable test assertions with FluentAssertions library |
| grpc | Defines gRPC services for peer-to-peer network communication |
| entity-framework | Handles Entity Framework Core database access and migrations |
| moq | Mocks dependencies in unit tests using Moq framework |
| cryptography | Applies multi-algorithm cryptography (ED25519, P-256, RSA-4096) |
| jwt | Implements JWT Bearer authentication for service-to-service authorization |
| nbitcoin | Utilizes NBitcoin for HD wallet operations (BIP32/39/44) |
| xunit | Writes unit tests with xUnit framework across 30 test projects |
| docker | Manages Docker containerization and docker-compose orchestration |
| playwright | Develops end-to-end UI tests with Playwright for Blazor applications |
| frontend-design | Styles Blazor WASM components with CSS and responsive design patterns |
| sorcha-cli | Builds and maintains the Sorcha CLI tool using System.CommandLine 2.0.2, Refit HTTP clients, and Spectre.Console. Use when creating CLI commands, adding options/arguments, implementing Refit service clients, writing CLI tests, or fixing command structure issues |
| sorcha-ui | Builds Sorcha.UI Blazor WASM pages with accompanying Playwright E2E tests using the Docker test infrastructure |
| blueprint-builder | Creates blueprint JSON templates, defines participants/actions/routes/schemas, configures cycle detection, troubleshoots blueprint publishing |
| walkthrough-builder | Architects, builds, and runs walkthroughs using the autonomous actor agent framework. Creates actor definitions, launcher scripts, and cross-register credential flows |
| network-bootstrap | Bootstraps the Sorcha network: genesis ceremony, n1.sorcha.dev deployment, validator key import, and platform setup |
