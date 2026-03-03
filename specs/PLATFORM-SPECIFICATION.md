# Sorcha Platform Specification

> **Purpose:** This document consolidates all Sorcha platform specifications into a single reference. It is structured for consumption by AI build systems that need to understand Sorcha's architecture, data models, API contracts, and business rules to reimplement the platform in another language or framework.

**Version:** 1.0
**Date:** 2026-03-03
**Source:** Consolidated from 48 feature specifications in `specs/`

---

## Table of Contents

1. [Platform Overview](#1-platform-overview)
2. [Architectural Principles](#2-architectural-principles)
3. [Service Architecture](#3-service-architecture)
4. [Data Models](#4-data-models)
5. [API Contracts](#5-api-contracts)
6. [Security Model](#6-security-model)
7. [Blueprint System](#7-blueprint-system)
8. [Distributed Ledger](#8-distributed-ledger)
9. [Cryptographic Wallet](#9-cryptographic-wallet)
10. [Peer Network](#10-peer-network)
11. [Validation & Consensus](#11-validation--consensus)
12. [User Interface](#12-user-interface)
13. [CLI Tool](#13-cli-tool)
14. [Cross-Cutting Concerns](#14-cross-cutting-concerns)
15. [Feature Catalog](#15-feature-catalog)

---

## 1. Platform Overview

Sorcha is a distributed ledger platform for secure, multi-participant data flow orchestration. It enables organizations to define structured workflows (blueprints) where multiple parties exchange, validate, and record data with cryptographic guarantees.

### Core Concepts

| Concept | Definition |
|---------|------------|
| **Blueprint** | A declarative JSON document defining a multi-party workflow: participants, actions, schemas, routing rules, and disclosure policies |
| **Register** | A distributed ledger instance — an immutable, append-only chain of signed transaction dockets |
| **Docket** | A block of validated transactions sealed with a Merkle tree root hash and signed by the validator |
| **Transaction** | A single action execution record: signed payload, sender/recipient addresses, timestamps |
| **Wallet** | A cryptographic key container supporting HD derivation (BIP32/39/44) and multiple algorithms |
| **Participant** | A named role in a blueprint workflow, linked to a user identity and wallet address |
| **Action** | A single step in a blueprint workflow, assigned to a participant, with schema validation and routing |
| **Disclosure** | Field-level access control: which fields of a payload each participant can see |

### DAD Security Model

- **Disclosure** — Field-level envelope encryption with per-recipient key wrapping (XChaCha20-Poly1305 symmetric, asymmetric key wrapping per recipient). Disclosure groups define which participants see which JSON Pointer paths.
- **Alteration** — Every data change produces a cryptographically signed transaction recorded on an immutable ledger. Transactions are chained via previous-hash references.
- **Destruction** — Peer network replication ensures no single point of failure. Registers are replicated across multiple nodes via gRPC streaming.

---

## 2. Architectural Principles

Source: `.specify/constitution.md`

1. **Microservice Boundaries** — Each service owns its data and exposes it only through APIs. No shared databases.
2. **Immutability** — Ledger data is append-only. No updates or deletes on transaction records.
3. **Stateless Execution** — The blueprint engine is stateless and portable (runs on server and in browser via WASM).
4. **Storage Abstraction** — All persistence goes through `IRepository<T>` interfaces with pluggable backends (EF Core, MongoDB, Redis, InMemory).
5. **Cryptographic Verification** — All ledger reads are verified against stored hashes before use.
6. **API Gateway Pattern** — All external traffic enters through a single YARP reverse proxy.
7. **Event-Driven Notifications** — SignalR hubs with Redis backplane for real-time updates.
8. **Central Package Management** — All NuGet packages versioned centrally in `Directory.Packages.props`.

---

## 3. Service Architecture

### Services

| Service | Responsibility | Database | Key Protocols |
|---------|---------------|----------|---------------|
| **API Gateway** | YARP reverse proxy, route aggregation, CORS, rate limiting | None | HTTP/HTTPS |
| **Blueprint Service** | Workflow CRUD, action submission, execution engine, SignalR notifications | MongoDB | REST, SignalR |
| **Register Service** | Distributed ledger storage, transaction queries, OData, governance | MongoDB | REST, OData, SignalR, gRPC |
| **Wallet Service** | HD wallet management, key generation, signing, encryption/decryption | PostgreSQL (EF Core) | REST, gRPC |
| **Tenant Service** | Multi-tenant auth, JWT issuer, user/org management, participant identity | PostgreSQL (EF Core) | REST |
| **Validator Service** | Transaction validation, memory pool, docket building, consensus | Redis (mempool) | REST, gRPC |
| **Peer Service** | P2P network topology, register replication, gRPC streaming | PostgreSQL (EF Core) | gRPC |

### Communication Patterns

```
External → API Gateway → Service (REST)
Service → Service: HTTP via ServiceClients (with JWT client credentials)
Service → Service: gRPC (for high-throughput operations)
Service → Client: SignalR WebSocket (real-time notifications)
Service → Service: Redis Pub/Sub (event distribution)
```

### Service Discovery

Uses .NET Aspire service discovery. Services reference each other by logical name (e.g., `https+http://wallet-service`). In Docker, DNS resolution via Docker network. In Aspire, automatic endpoint resolution.

---

## 4. Data Models

### Blueprint Models (`Sorcha.Blueprint.Models`)

```
Blueprint
├── Id: Guid
├── Title: string
├── Description: string
├── Version: string (semver)
├── Status: BlueprintStatus (Draft, Published, Archived)
├── Participants: List<Participant>
│   ├── Role: string (unique within blueprint)
│   ├── Description: string
│   └── Permissions: List<string>
├── Actions: List<Action>
│   ├── Id: int (sequential)
│   ├── Title: string
│   ├── AssignedTo: string (participant role)
│   ├── Schema: JsonElement (JSON Schema Draft 2020-12)
│   ├── Routing: RoutingRules
│   │   └── Conditions: List<RoutingCondition>
│   │       ├── If: JsonLogic expression
│   │       └── Then: string (next action ID or "complete")
│   ├── Disclosure: DisclosureRules
│   │   └── Groups: List<DisclosureGroup>
│   │       ├── Participants: List<string> (roles)
│   │       └── Fields: List<string> (JSON Pointers)
│   ├── Calculations: List<Calculation>
│   │   ├── Target: string (JSON Pointer)
│   │   └── Expression: JsonLogic expression
│   └── Validations: List<Validation>
│       ├── Expression: JsonLogic expression
│       └── Message: string
├── Schemas: Dictionary<string, JsonElement>
└── Metadata: Dictionary<string, object>
```

### Register Models (`Sorcha.Register.Models`)

```
Register
├── Id: Guid
├── Name: string
├── Description: string
├── Status: RegisterStatus (Active, Suspended, Archived)
├── GenesisHash: string (SHA-256 hex, 64 chars)
├── LatestDocketNumber: long
├── CreatedAt: DateTimeOffset
├── CryptoPolicy: CryptoPolicy
│   ├── AllowedAlgorithms: List<string>
│   └── MinKeySize: int
└── GovernanceRules: GovernancePolicy

TransactionModel
├── TxId: string (SHA-256 hex, 64 chars — NOT a GUID)
├── RegisterId: Guid
├── Type: TransactionType (Control=0, Action=1, Governance=2)
├── SenderAddress: string (wallet address)
├── RecipientAddresses: List<string>
├── Payload: byte[] (encrypted or plain)
├── PayloadHash: string (SHA-256 of plaintext)
├── Signature: string (base64)
├── Algorithm: string (e.g., "ED25519")
├── PreviousTxHash: string
├── Timestamp: DateTimeOffset
├── DocketNumber: long? (null until sealed)
└── Metadata: Dictionary<string, string>

Docket
├── DocketNumber: long (sequential per register)
├── RegisterId: Guid
├── Transactions: List<TransactionModel>
├── MerkleRoot: string (SHA-256)
├── PreviousDocketHash: string
├── Hash: string (SHA-256 of docket content)
├── ValidatorSignature: string
├── ValidatorAddress: string
├── Timestamp: DateTimeOffset
└── TransactionCount: int
```

### Wallet Models (`Sorcha.Wallet.Core`)

```
Wallet
├── WalletId: Guid
├── Name: string
├── Address: string (Bech32m encoded, ws2 prefix)
├── Algorithm: WalletNetworks (ED25519, NISTP256, RSA4096, MLDSA65, MLKEM768, SLHDSA128S)
├── PublicKey: byte[]
├── EncryptedPrivateKey: byte[] (AES-GCM encrypted at rest)
├── Status: WalletStatus (Active, Suspended, Deleted)
├── TenantId: string
├── AccessLevel: AccessLevel (Owner, ReadWrite, ReadOnly)
├── CreatedAt: DateTimeOffset
├── UpdatedAt: DateTimeOffset
└── MnemonicSalt: byte[] (for HD derivation — NOT stored, user responsibility)
```

### Tenant Models (`Sorcha.Tenant.Models`)

```
User
├── Id: Guid
├── Email: string
├── PasswordHash: string (Argon2id)
├── DisplayName: string
├── Status: UserStatus (Active, Suspended, Deleted)
├── OrganizationId: Guid
└── Roles: List<string>

Organization
├── Id: Guid
├── Name: string
├── Subdomain: string (unique)
├── Status: OrgStatus (Active, Suspended)
└── Settings: OrgSettings

ServicePrincipal
├── Id: Guid
├── ClientId: string (unique)
├── ClientSecretHash: string (SHA-256)
├── Name: string
├── Scopes: List<string>
└── OrganizationId: Guid

ParticipantIdentity
├── Id: Guid
├── UserId: Guid
├── OrganizationId: Guid
├── DisplayName: string
├── Status: ParticipantStatus (Active, Suspended)
└── LinkedWallets: List<LinkedWalletAddress>
    ├── WalletAddress: string
    ├── VerifiedAt: DateTimeOffset
    └── Status: LinkStatus (Active, Revoked)
```

### Encrypted Payload Models

```
EncryptedEnvelope
├── EncryptedPayload: byte[] (XChaCha20-Poly1305)
├── SymmetricKeyNonce: byte[] (24 bytes)
├── WrappedKeys: List<RecipientKeyWrap>
│   ├── RecipientAddress: string
│   ├── Algorithm: string (P-256 ECIES, RSA-OAEP, ML-KEM)
│   ├── WrappedKey: byte[]
│   └── Nonce: byte[] (algorithm-specific)
├── DisclosureGroup: string (group name)
├── PayloadHash: string (SHA-256 of plaintext)
└── ContentType: string (MIME type)
```

---

## 5. API Contracts

### Authentication Endpoints (Tenant Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/auth/login` | User login (returns JWT access + refresh tokens) |
| POST | `/api/auth/refresh` | Refresh access token |
| POST | `/api/auth/revoke` | Revoke tokens |
| POST | `/api/service-auth/token` | Service-to-service OAuth2 client credentials |
| POST | `/api/bootstrap` | System initialization (first-run setup) |

### Organization & User Management (Tenant Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET/POST | `/api/organizations` | List/Create organizations |
| GET/PUT/DELETE | `/api/organizations/{id}` | CRUD organization |
| GET | `/api/organizations/{id}/stats` | Organization statistics |
| GET/POST | `/api/organizations/{id}/users` | List/Create users |
| GET/POST | `/api/organizations/{id}/service-principals` | List/Create service principals |

### Participant Identity (Tenant Service via Gateway)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/organizations/{orgId}/participants` | Register participant |
| GET | `/api/organizations/{orgId}/participants` | List participants |
| POST | `/api/participants/{id}/wallet-links` | Initiate wallet link (challenge-response) |
| POST | `/api/participants/{id}/wallet-links/{challengeId}/verify` | Verify wallet signature |
| POST | `/api/me/register-participant` | Self-register |

### Blueprint Management (Blueprint Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET/POST | `/api/blueprints` | List/Create blueprints |
| GET/PUT/DELETE | `/api/blueprints/{id}` | CRUD blueprint |
| POST | `/api/blueprints/{id}/publish` | Publish blueprint to register |
| GET | `/api/blueprints/{id}/actions` | List actions for blueprint |
| POST | `/api/blueprints/{id}/actions/{actionId}/submit` | Submit action |
| POST | `/api/blueprints/{id}/actions/{actionId}/reject` | Reject action |
| POST | `/api/execution/validate` | Validate payload against schema |
| POST | `/api/execution/calculate` | Evaluate calculations |
| POST | `/api/execution/route` | Evaluate routing rules |
| POST | `/api/execution/disclose` | Apply disclosure rules |

### Wallet Operations (Wallet Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET/POST | `/api/wallets` | List/Create wallets |
| GET/DELETE | `/api/wallets/{address}` | Get/Delete wallet |
| POST | `/api/wallets/{address}/sign` | Sign data |
| POST | `/api/wallets/{address}/verify` | Verify signature |
| POST | `/api/wallets/{address}/encrypt` | Encrypt payload |
| POST | `/api/wallets/{address}/decrypt` | Decrypt payload |
| GET | `/api/wallets/{address}/public-key` | Get public key |

### Register Operations (Register Service)

| Method | Path | Purpose |
|--------|------|---------|
| GET/POST | `/api/registers` | List/Create registers |
| GET | `/api/registers/{id}` | Get register details |
| GET | `/api/registers/{id}/transactions` | Query transactions (OData) |
| GET | `/api/registers/{id}/transactions/{txId}` | Get transaction |
| GET | `/api/registers/{id}/dockets` | List dockets |
| GET | `/api/registers/{id}/dockets/{number}` | Get docket |
| POST | `/api/registers/{id}/governance/propose` | Propose governance change |
| POST | `/api/registers/{id}/governance/vote` | Vote on proposal |
| GET | `/api/registers/{id}/participants` | List published participants |

### Validator Operations (Validator Service)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/validator/submit` | Submit transaction for validation |
| GET | `/api/validator/mempool/{registerId}` | View memory pool |
| GET | `/api/validator/status` | Validator health and status |

### Notification Endpoints (Wallet Service — gRPC)

| Service | RPC | Purpose |
|---------|-----|---------|
| WalletNotification | NotifyInboundTransaction | Single transaction notification |
| WalletNotification | NotifyInboundTransactionBatch | Batch notification |
| WalletNotification | GetNotificationPreferences | User notification preferences |
| RegisterAddress | SyncAddresses | Sync local wallet addresses to bloom filter |
| RegisterAddress | GetAddresses | Get addresses for a register |

### Peer Network (Peer Service — gRPC)

| Service | RPC | Purpose |
|---------|-----|---------|
| RegisterSync | PullDocketChain | Pull docket chain from peer |
| RegisterSync | PullDocketTransactions | Pull transactions for a docket |
| RegisterSync | SubscribeToRegister | Live subscription to register updates |
| RegisterSync | GetRegisterSyncStatus | Get replication status |
| TransactionDistribution | NotifyTransaction | Notify peers of new transaction |
| TransactionDistribution | GetTransaction | Retrieve transaction by ID |
| TransactionDistribution | StreamTransaction | Stream transaction data |

---

## 6. Security Model

### Authentication

- **User auth**: Email/password → JWT access token (60 min) + refresh token (24 hours)
- **Service auth**: OAuth2 client credentials → JWT access token (8 hours)
- **Delegation**: Services can request delegation tokens to act on behalf of users
- **Token validation**: Hybrid — local JWT signature verification + optional token introspection
- **Token revocation**: Redis-backed revocation store checked on each request
- **Password hashing**: Argon2id

### Authorization Policies

| Policy | Required Claims |
|--------|----------------|
| `Authenticated` | Valid JWT token |
| `AdminOnly` | `role: admin` |
| `OrgAdmin` | `role: org-admin` + matching `org_id` |
| `ServiceAccount` | `client_id` claim (service principal) |
| `RegisterAccess` | `scope: registers:read` or `registers:write` |
| `WalletAccess` | `scope: wallets:read` or `wallets:sign` |

### Encryption

- **At rest**: Wallet private keys encrypted with AES-GCM using Data Protection API
- **In transit**: TLS (HTTPS for REST, TLS for gRPC)
- **Payload encryption**: XChaCha20-Poly1305 symmetric + asymmetric key wrapping per recipient
- **Key wrapping algorithms**: P-256 ECIES, RSA-OAEP-256, ML-KEM-768

---

## 7. Blueprint System

### Execution Engine Pipeline

The blueprint engine processes actions in a 4-step pipeline:

1. **Validate** — Evaluate payload against JSON Schema (Draft 2020-12) using `JsonSchema.Net`. Requires `JsonElement` input (not `JsonNode`).
2. **Calculate** — Evaluate JSON Logic expressions to compute derived fields. Supports arithmetic, string manipulation, array operations, and conditional logic.
3. **Route** — Evaluate routing conditions (JSON Logic) to determine the next action and participant. Supports conditional branching, loops, and completion.
4. **Disclose** — Apply disclosure rules using JSON Pointers (RFC 6901) to produce per-participant views of the payload.

### Engine Characteristics

- **Stateless**: No mutable state — all inputs and outputs are immutable
- **Portable**: Runs identically on .NET server and Blazor WASM client
- **Thread-safe**: Immutable design allows concurrent execution
- **Schema caching**: Compiled schemas cached with `SemaphoreSlim`-guarded lazy initialization

### Blueprint Lifecycle

```
Draft → Published (to register) → Active (accepting actions) → Completed/Archived
```

Publishing a blueprint creates a genesis transaction on the target register containing the blueprint definition as a control record.

---

## 8. Distributed Ledger

### Register Structure

Each register maintains an independent chain:

```
Genesis Docket (0) → Docket 1 → Docket 2 → ... → Docket N
                      │           │                 │
                      ├─ Tx A     ├─ Tx D           ├─ Tx G
                      ├─ Tx B     └─ Tx E           └─ Tx H
                      └─ Tx C
```

### Transaction Types

| Type | Value | Purpose |
|------|-------|---------|
| Control | 0 | Register creation, blueprint publishing, governance |
| Action | 1 | Blueprint action execution (user workflow data) |
| Governance | 2 | Governance proposals and votes |

### TxId Generation

Transaction IDs are SHA-256 hashes (64-character hex strings), NOT GUIDs. Generated from:
```
SHA-256(senderAddress + payload + timestamp + previousTxHash)
```

### DID URI Addressing

Transactions are addressable via DID URIs:
```
did:sorcha:register:{registerId}/tx:{txId}
```

### Governance

Registers support on-ledger governance:
- Governance proposals are submitted as governance transactions
- Participants vote on proposals
- Majority (>50%) required for approval
- Governance rules are defined in the register's genesis docket

### Inbound Transaction Routing

When transactions arrive at a node, a Bloom filter index checks if any local wallet addresses are recipients. Matches trigger notifications to the wallet owner via SignalR (real-time) or digest (batched email/push).

Recovery service processes historical dockets on startup to catch missed notifications.

---

## 9. Cryptographic Wallet

### Supported Algorithms

| Algorithm | Key Size | Use Case |
|-----------|----------|----------|
| ED25519 | 256-bit | Default signing, fast verification |
| NISTP-256 (P-256) | 256-bit | ECIES key wrapping, ECDSA signing |
| RSA-4096 | 4096-bit | Legacy compatibility, RSA-OAEP encryption |
| ML-DSA-65 | Post-quantum | Quantum-safe digital signatures |
| ML-KEM-768 | Post-quantum | Quantum-safe key encapsulation |
| SLH-DSA-128s | Post-quantum | Stateless hash-based signatures |
| BLS12-381 | Pairing-based | Threshold signatures (deferred) |

### HD Wallet Derivation

Follows BIP32/39/44 standards:
- **BIP39**: Mnemonic generation (12/24 words) — NOT stored by platform
- **BIP32**: Hierarchical deterministic key derivation
- **BIP44**: Purpose/coin-type path structure: `m/44'/0'/account'/change/index`

### Address Format

Wallet addresses use Bech32m encoding with `ws2` prefix:
```
ws2_1qpz... (similar to Bitcoin SegWit v2 addresses)
```

---

## 10. Peer Network

### Topology

Hub-and-spoke with peer-to-peer capability:
- **Hub nodes**: Always-on infrastructure nodes that maintain full register copies
- **Peer nodes**: Connect to hubs, may maintain partial register copies
- **Heartbeat**: Periodic health checks between connected peers

### Register Replication

1. New peer joins → connects to seed peer via gRPC
2. Peer subscribes to registers of interest
3. Full sync: `PullDocketChain` → `PullDocketTransactions` for each docket
4. Live sync: `SubscribeToRegister` for real-time docket streaming
5. New transactions: `NotifyTransaction` broadcast to connected peers

### Recovery

When a peer detects it's behind the network head:
1. Calculate gap: `networkHeadDocket - localLatestDocket`
2. Pull missing dockets sequentially
3. Validate each docket before applying
4. Resume live subscription

---

## 11. Validation & Consensus

### Memory Pool

- Redis-backed sorted set per register
- Transactions ordered by arrival time
- Duplicate detection via SHA-256 TxId set index (O(1) lookup)
- FIFO processing with priority override capability

### Docket Building

Triggered by either:
- **Time threshold**: Configurable interval (default: 30 seconds)
- **Size threshold**: Configurable transaction count (default: 100)

Process:
1. Drain transactions from memory pool
2. Validate each transaction against blueprint rules
3. Compute Merkle tree root of transaction hashes
4. Create docket with sequential number, previous docket hash
5. Sign docket with system wallet
6. Submit to register service
7. Broadcast to peers via peer service

### Consensus (MVD — Simplified)

Current MVD uses single-validator model:
- Validator service owns system wallet
- Signs dockets unilaterally
- No multi-validator consensus yet

Deferred: Leader election, multi-validator BFT, fork detection

---

## 12. User Interface

### Technology

Blazor WebAssembly (client-side .NET in browser) with MudBlazor component library.

### Key Pages

| Page | Purpose |
|------|---------|
| Dashboard | Organization overview, service health, recent activity |
| Blueprints | Browse, create, edit, publish blueprints |
| Blueprint Designer | Visual drag-and-drop blueprint editor |
| Registers | Browse registers, view transactions, query data |
| Wallets | Manage wallets, view balances, sign operations |
| Actions | View pending actions, submit/reject workflow steps |
| Settings | User preferences, organization settings |
| Admin | Service health, user management, org management |

### Real-Time Updates

SignalR connections for:
- Action notifications (new action assigned to user)
- Register events (new docket sealed, transaction added)
- Blueprint state changes (published, action completed)
- Inbound transaction alerts (transaction targeting user's wallet)

### Internationalization

i18n wiring for Home, Settings, and MainLayout. Resource files for string localization.

---

## 13. CLI Tool

### Technology

- `System.CommandLine` 2.0.2 for command parsing
- `Refit` for typed HTTP clients
- `Spectre.Console` for rich terminal output

### Command Structure

```
sorcha
├── auth
│   ├── login [--username] [--password] [--client-id] [--client-secret]
│   ├── status [--profile]
│   └── logout [--all]
├── org
│   ├── list [--profile]
│   ├── get --org-id <id>
│   └── create --name <name> --subdomain <subdomain>
├── user
│   ├── list --org-id <id>
│   └── get --username <email>
├── wallet
│   ├── list
│   ├── create --name <name> --algorithm <alg>
│   ├── get --address <addr>
│   └── sign --address <addr> --data <base64>
├── register
│   ├── list
│   └── get --register-id <id>
├── tx
│   ├── list --register-id <id>
│   └── submit --register-id <id> --payload <json>
└── peer
    ├── list [--status <status>]
    ├── get --peer-id <id>
    ├── topology [--format tree|json]
    └── stats [--window <duration>]
```

### Global Options

```
--profile, -p    Configuration profile (dev, staging, production)
--output, -o     Output format (table, json, csv)
--quiet, -q      Suppress non-essential output
--verbose, -v    Enable verbose logging
```

### Token Storage

Platform-specific secure storage:
- Windows: DPAPI
- macOS: Keychain
- Linux: Encrypted file with user-specific key

---

## 14. Cross-Cutting Concerns

### Storage Abstraction

All persistence uses `IRepository<T>` from `Sorcha.Storage.Abstractions`:

```
IRepository<T>
├── GetByIdAsync(id)
├── GetAllAsync(filter, skip, take)
├── AddAsync(entity)
├── UpdateAsync(entity)
├── DeleteAsync(id)
├── ExistsAsync(id) → uses AnyAsync, not FindAsync
└── CountAsync(filter)

IUnitOfWork
├── BeginTransactionAsync()
├── CommitAsync()
└── RollbackAsync()
```

Implementations: EF Core (PostgreSQL), MongoDB, Redis (cache), InMemory (testing).

### Service Clients

All inter-service HTTP communication uses `Sorcha.ServiceClients`:
- Typed clients via `IHttpClientFactory`
- Automatic JWT token acquisition via client credentials
- Retry policies via Polly
- gRPC clients via `GrpcClientFactory` with named clients

### Observability

- OpenTelemetry for distributed tracing
- Structured logging (Serilog)
- Health checks per service (aggregated at gateway)
- Aspire dashboard for telemetry visualization
- Custom metrics via `System.Diagnostics.Metrics`

### Configuration

- `appsettings.json` per service
- Environment variables (override via `.env`)
- .NET Aspire service discovery
- JWT settings shared via `JwtSettings` configuration class

---

## 15. Feature Catalog

Complete list of all implemented features with their specification references.

### Foundation

| # | Feature | Status | Spec |
|---|---------|--------|------|
| 001 | Tenant Service Authentication | Complete | `specs/001-tenant-auth/` |
| 001 | Participant Identity Registry | Complete | `specs/001-participant-identity/` |
| 001 | Published Participant Records | Complete | `specs/001-participant-records/` |
| 001 | MCP Server for AI Assistants | Complete | `specs/001-mcp-server/` |
| 001 | AI-Assisted Blueprint Chat | Complete | `specs/001-blueprint-chat/` |
| 001 | Blueprint Designer Completion | Complete | `specs/001-designer-completion/` |
| 001 | UI Token Refresh | Complete | `specs/001-ui-token-refresh/` |
| 001 | Validator Service Wallet | Complete | `specs/001-validator-service-wallet/` |
| 001 | Hardware Crypto Enclaves | Deferred | `specs/001-hardware-crypto-enclaves/` |
| 002 | Storage Abstraction Layer | Complete | `specs/002-storage-abstraction/` |
| 002 | Validator Service | Complete | `specs/002-validator-service/` |

### Core Platform

| # | Feature | Status | Spec |
|---|---------|--------|------|
| 011 | Admin Dashboard | Complete | `specs/011-admin-dashboard/` |
| 012 | Registers & Transactions UI | Complete | `specs/012-registers-transactions-ui/` |
| 013 | System Schema Store | Complete | `specs/013-system-schema-store/` |
| 014 | Browser Crypto (ZKP) | Complete | `specs/014-client-zkp/` |
| 015 | Register Crypto Fix | Complete | `specs/015-fix-register-crypto/` |
| 016 | CLI Register Commands | Complete | `specs/016-cli-register-update/` |
| 017 | UI Register Management | Complete | `specs/017-ui-register-management/` |
| 018 | Blueprint Engine Integration | Complete | `specs/018-blueprint-engine-integration/` |
| 019 | Payload Encryption Spec | Complete | `specs/019-payload-encryption/` |
| 020 | Validator Engine Validation | Complete | `specs/020-validator-engine-validation/` |
| 021 | Transaction Query API (OData) | Complete | `specs/021-transaction-query-api/` |
| 022 | Resolve Runtime Stubs | Complete | `specs/022-resolve-runtime-stubs/` |
| 023 | Consolidate TX Versioning | Complete | `specs/023-consolidate-tx-versioning/` |

### Network & Peer

| # | Feature | Status | Spec |
|---|---------|--------|------|
| 024 | Peer Network Management | Complete | `specs/024-peer-network-management/` |
| 030 | Peer Advertisement Resync | Complete | `specs/030-peer-advertisement-resync/` |

### UI & UX

| # | Feature | Status | Spec |
|---|---------|--------|------|
| 025 | UI Modernization | Complete | `specs/025-ui-modernization/` |
| 029 | Blueprint Visual Designer | Complete | `specs/029-blueprint-visual-designer/` |
| 032 | Action Form Renderer | Complete | `specs/032-action-form-renderer/` |
| 033 | Wallet Dashboard Fixes | Complete | `specs/033-fix-wallet-dashboard-bugs/` |
| 037 | New Submission Page | Complete | `specs/037-new-submission-page/` |
| 046 | UI Polish & Blueprint Designer | Complete | `specs/046-ui-polish-designer/` |

### Ledger & Transactions

| # | Feature | Status | Spec |
|---|---------|--------|------|
| 026 | Register Creation Pipeline Fix | Complete | `specs/026-fix-register-creation-pipeline/` |
| 027 | Blueprint Template Library | Complete | `specs/027-blueprint-template-library/` |
| 028 | Transaction Pipeline Fix | Complete | `specs/028-fix-transaction-pipeline/` |
| 031 | Register Governance | Complete | `specs/031-register-governance/` |
| 034 | Schema Library | Complete | `specs/034-schema-library/` |
| 036 | Unified Transaction Submission | Complete | `specs/036-unified-transaction-submission/` |
| 038 | Content-Type Payload Encoding | Complete | `specs/038-content-type-payload/` |

### Security & Cryptography

| # | Feature | Status | Spec |
|---|---------|--------|------|
| 031 | Verifiable Credentials (SD-JWT VC) | Complete | `specs/031-verifiable-credentials/` |
| 039 | Verifiable Presentations | Complete | `specs/039-verifiable-presentations/` |
| 040 | Quantum-Safe Cryptography | Complete | `specs/040-quantum-safe-crypto/` |
| 041 | Auth Integration (all services) | Complete | `specs/041-auth-integration/` |
| 045 | Encrypted Payload Integration | Complete | `specs/045-encrypted-payload-integration/` |

### Infrastructure

| # | Feature | Status | Spec |
|---|---------|--------|------|
| 043 | UI/CLI Modernization | Complete | `specs/043-ui-cli-modernization/` |
| 044 | Codebase Consolidation | Complete | `specs/044-codebase-consolidation/` |
| 047 | Inbound TX Routing & Notifications | Complete | `specs/047-inbound-tx-routing/` |

---

## Appendix: Technology Mapping

For reimplementation in another language, these are the key technology choices and their purposes:

| .NET Technology | Purpose | Portable Alternative |
|-----------------|---------|---------------------|
| ASP.NET Core Minimal APIs | REST endpoints | Express, FastAPI, Gin, Axum |
| Entity Framework Core | PostgreSQL ORM | SQLAlchemy, Prisma, GORM, Diesel |
| MongoDB.Driver | MongoDB access | Mongoose, PyMongo, mongo-go-driver |
| StackExchange.Redis | Redis client | ioredis, redis-py, go-redis |
| SignalR | WebSocket real-time | Socket.IO, WebSocket, Tungstenite |
| YARP | Reverse proxy | Nginx, Envoy, Traefik |
| Grpc.AspNetCore | gRPC server | grpc-node, grpcio, tonic |
| NBitcoin | HD wallets (BIP32/39/44) | bitcoinjs-lib, python-hdwallet |
| JsonSchema.Net | JSON Schema validation | ajv, jsonschema, serde-json-schema |
| JsonLogic.Net | JSON Logic evaluation | json-logic-js, json-logic-py |
| System.CommandLine | CLI framework | Commander, Click, Cobra, Clap |
| MudBlazor | UI components | Material UI, Vuetify, Tailwind |
| xUnit + FluentAssertions | Testing | Jest, pytest, go test |

---

*This specification was consolidated from 48 feature directories in `specs/`, architectural documents in `.specify/`, and API documentation in `docs/`. For detailed implementation of any specific feature, refer to the individual spec directory listed in the Feature Catalog.*
