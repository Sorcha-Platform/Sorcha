# Sorcha Validator Service

**Version:** 1.0
**Status:** Complete (100% MVD)
**Location:** `src/Services/Sorcha.Validator.Service/`
**Last Updated:** 2026-03-01

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Key Features](#key-features)
4. [API Endpoints](#api-endpoints)
5. [gRPC Services](#grpc-services)
6. [Components](#components)
7. [Configuration](#configuration)
8. [Data Models](#data-models)
9. [Testing](#testing)
10. [Deployment](#deployment)
11. [Development](#development)

---

## Overview

The Validator Service is the **blockchain consensus and validation component** of the Sorcha platform. It implements the distributed ledger consensus mechanism, building and validating Dockets (blocks) that contain Transactions from Blueprint Action executions.

### Purpose

- **Docket Building** - Assembles Transactions from the memory pool into cryptographically-linked blocks
- **Transaction Validation** - Verifies signatures, schemas, and business rules
- **Distributed Consensus** - Coordinates validation across peer validator nodes
- **Genesis Management** - Creates first blocks for new Registers
- **Operational Control** - Provides admin APIs for metrics, monitoring, and lifecycle management

### Strategic Role

The Validator Service serves as the **trust anchor** of Sorcha, ensuring:
- **Data Integrity** through cryptographic hashing (SHA-256 + Merkle trees)
- **Authentication** through signature verification (via Wallet Service)
- **Consensus** through distributed validation (quorum-based voting)
- **Immutability** through blockchain chaining (PreviousHash linkage)

---

## Architecture

### Layered Design

```
┌─────────────────────────────────────────────────────────────┐
│                  Sorcha.Validator.Service                   │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  API Layer (REST + gRPC)                                     │
│  ┌──────────────┬──────────────┬──────────────┐            │
│  │ Validation   │ Admin        │ gRPC Peer    │            │
│  │ Endpoints    │ Endpoints    │ Communication│            │
│  └──────────────┴──────────────┴──────────────┘            │
│                         ↓                                    │
│  Service Layer                                               │
│  ┌─────────────────────────────────────────────────┐       │
│  │ ValidatorOrchestrator (Pipeline Coordinator)    │       │
│  └─────────────────────────────────────────────────┘       │
│           ↓              ↓              ↓                   │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐          │
│  │ Docket      │ │ Consensus   │ │ MemPool     │          │
│  │ Builder     │ │ Engine      │ │ Manager     │          │
│  └─────────────┘ └─────────────┘ └─────────────┘          │
│                                                               │
│  Core Layer (Portable - Enclave-Safe)                       │
│  ┌─────────────────────────────────────────────────┐       │
│  │ Sorcha.Validator.Core                           │       │
│  │ - Pure validation logic (no I/O)                │       │
│  │ - Thread-safe, stateless, deterministic         │       │
│  └─────────────────────────────────────────────────┘       │
│                                                               │
└─────────────────────────────────────────────────────────────┘
         ↓                  ↓                  ↓
   Wallet Service    Register Service    Peer Service
  (Signatures)        (Storage)          (Broadcast)
```

### Technology Stack

- **.NET 10.0** - Target framework
- **ASP.NET Core Minimal APIs** - RESTful endpoints
- **gRPC** - Peer-to-peer validator communication
- **Redis** - Distributed coordination and caching
- **.NET Aspire** - Service orchestration and observability
- **Scalar** - Interactive API documentation
- **OpenTelemetry** - Metrics, logging, and tracing

---

## Key Features

### ✅ Implemented (100% MVD)

1. **Memory Pool Management**
   - FIFO + priority queues (High/Normal/Low)
   - Per-register isolation with capacity limits
   - Automatic eviction and expiration
   - Background cleanup service

2. **Docket Building**
   - Hybrid triggers (time-based OR size-based)
   - Merkle tree computation for transaction integrity
   - SHA-256 docket hashing with previous hash linkage
   - Cryptographic signatures via Wallet Service

3. **Distributed Consensus**
   - Quorum-based voting (configurable threshold >50%)
   - Parallel gRPC vote collection from peer validators
   - Timeout handling with graceful degradation
   - Signature verification for all votes

4. **Validator Orchestration**
   - Full pipeline coordination (MemPool → Build → Consensus → Persist)
   - Per-register validator state tracking
   - Start/stop/status admin control
   - Manual pipeline execution for testing

5. **gRPC Peer Communication**
   - `RequestVote` RPC for consensus voting
   - `ValidateDocket` RPC for peer docket validation
   - `GetHealthStatus` RPC for health monitoring
   - Protobuf-based efficient serialization

6. **REST Admin API**
   - Start/stop validators by register
   - Query validator status and statistics
   - Manual pipeline processing
   - Memory pool statistics

7. **Background Services**
   - MemPoolCleanupService (expired transaction removal)
   - DocketBuildTriggerService (automatic docket building)

### Deferred (Post-MVD)

- Fork detection and chain recovery
- Enclave support (Intel SGX, AMD SEV, Azure Confidential Computing)
- Decentralized consensus (multi-node quorum)
- Persistent memory pool state (Redis/PostgreSQL)
- Enhanced observability (custom metrics)

---

## API Endpoints

### Validation Endpoints

#### POST /api/v1/transactions/validate
Validates a transaction and adds it to the memory pool.

**Request Body:**
```json
{
  "transactionId": "tx_abc123",
  "registerId": "reg_001",
  "blueprintId": "bp_supply_chain",
  "actionId": "action_001",
  "payload": { "item": "Widget A", "quantity": 100 },
  "payloadHash": "sha256_hash_here",
  "signatures": [
    {
      "publicKey": "0x1234...abcd",
      "signatureValue": "sig_value_here",
      "algorithm": "ED25519"
    }
  ],
  "createdAt": "2025-12-22T10:00:00Z",
  "expiresAt": "2025-12-23T10:00:00Z",
  "priority": "Normal",
  "metadata": {}
}
```

**Response (200 OK):**
```json
{
  "isValid": true,
  "added": true,
  "transactionId": "tx_abc123",
  "registerId": "reg_001",
  "addedAt": "2025-12-22T10:00:05Z"
}
```

**Response (400 Bad Request):**
```json
{
  "isValid": false,
  "errors": [
    {
      "code": "SIGNATURE_INVALID",
      "message": "Transaction signature verification failed",
      "field": "signatures[0].signatureValue"
    }
  ]
}
```

#### GET /api/v1/transactions/mempool/{registerId}
Gets memory pool statistics for a register.

**Response (200 OK):**
```json
{
  "registerId": "reg_001",
  "totalTransactions": 42,
  "highPriorityCount": 5,
  "normalPriorityCount": 30,
  "lowPriorityCount": 7,
  "isFull": false,
  "fillPercentage": 0.42,
  "oldestTransactionAge": "00:05:30",
  "totalEvictions": 3
}
```

---

### Admin Endpoints

#### POST /api/admin/validators/start
Starts the validation pipeline for a register.

**Request Body:**
```json
{
  "registerId": "reg_001"
}
```

**Response (200 OK):**
```json
{
  "registerId": "reg_001",
  "status": "Started",
  "message": "Validator started for register reg_001"
}
```

#### POST /api/admin/validators/stop
Stops the validation pipeline for a register.

**Request Body:**
```json
{
  "registerId": "reg_001",
  "persistMemPool": true
}
```

**Response (200 OK):**
```json
{
  "registerId": "reg_001",
  "status": "Stopped",
  "message": "Validator stopped for register reg_001",
  "memPoolPersisted": true
}
```

#### GET /api/admin/validators/{registerId}/status
Gets the current status of a validator.

**Response (200 OK):**
```json
{
  "registerId": "reg_001",
  "isActive": true,
  "startedAt": "2025-12-22T09:00:00Z",
  "docketsBuilt": 125,
  "lastDocketNumber": 124,
  "lastDocketTimestamp": "2025-12-22T10:30:00Z",
  "memPoolSize": 42
}
```

#### POST /api/admin/validators/{registerId}/process
Manually triggers a single validation pipeline iteration (for testing/debugging).

**Response (200 OK):**
```json
{
  "docketNumber": 125,
  "consensusAchieved": true,
  "writtenToRegister": true,
  "duration": "00:00:03.5",
  "errorMessage": null,
  "transactionCount": 50
}
```

**Response (200 OK - No docket built):**
```json
{
  "message": "No docket was built (triggers not met or no pending transactions)",
  "registerId": "reg_001"
}
```

---

## Transaction-type carve-outs must come from SIGNED data

`TransactionTypeClassifier` decides which transactions are exempt from certain rules. Three
carve-outs matter for presentation-lifecycle transactions:

| Predicate | Waives |
|-----------|--------|
| `IsLifecycleTransaction` | the action-data schema check (`VAL_SCHEMA_001/002/003/004`) |
| `IsIntraActionLifecycleTerminal` | the routing-decision attestation, and `VAL_BP_003` route reachability |

**These predicates read the `type` field inside the transaction's `Payload`, never
`Metadata["Type"]` — and new carve-outs must do the same.**

`Metadata` is **not** signed. The signed data is `"{TransactionId}:{PayloadHash}"`;
`PayloadHash` covers **only** `Payload`; and the docket merkle leaf does not include metadata
either. So anything that can submit a transaction can set `Metadata["Type"]` freely with nothing
detecting the change — no signature failure, no hash mismatch, no log.

Until the 2026-07-29 catch-up security review these predicates keyed on `Metadata["Type"]`, so
adding one unsigned string (`Metadata["Type"] = "PresentationInitiated"`) to **any** transaction
disabled the schema check, the routing attestation and the reachability check at once, and it
sealed normally. `Payload.type` is inside the hash the signature covers, so it is the only
trustworthy discriminator. (`IsRejectionTransaction` already consulted the payload for the same
reason — it was the precedent, not the exception.)

Genuine lifecycle transactions always carry it: `TransactionBuilderServiceExtensions
.BuildPresentationInitiatedAsync` / `BuildPresentationOutcomeAsync` /
`BuildPresentationAbandonedAsync` each write `type` as the first payload property **before**
signing (`presentation-initiated`, `presentation-outcome`, `presentation-abandoned`). The
PascalCase `Metadata["Type"]` values still exist for the docket-build trigger's own purposes;
they are simply not authoritative for validation carve-outs.

When metadata claims a lifecycle type the signed payload does not corroborate, the exemption is
refused **and** `ValidationEngine` logs a warning — a transaction requesting an exemption it is
not entitled to is what an attempted schema-validation bypass looks like on the wire.

## Administrative exemptions come from PROVED AUTHORITY (Feature 196 / #1591)

The section above is about *which transactions* are carved out. This one is about *who may be*.

Three administrative kinds waive **six** rules at once — action-schema validation, blueprint
conformance (**including `VAL_BP_002` sender authorisation**), the routing-decision attestation,
crypto policy, sequence replay, and (via the persisted transaction type) fork detection:

| Kind | Authority that must be proved |
|------|-------------------------------|
| `Genesis` | the constant genesis transaction id, on the system register, signed by a key whose fingerprint matches this node's `INodeTrustAnchor` |
| `Control` | the signer is on the register's **governance** roster |
| `BlueprintPublish` | the signer is on the register's **validator** roster under `sorcha:blueprint-publish` |

`IExemptionAuthorityResolver` is the **single producer** of that decision. Nothing else may grant an
exemption.

**What was wrong.** The grant used to come from `Metadata["Type"]` or `BlueprintId == "genesis"` —
both unsigned, exactly as the lifecycle section above describes. `Control` happened to be covered by
a roster check keyed on the same string, so claiming it was a trade; `Genesis` and `BlueprintPublish`
substituted nothing at all. Because one of the six waivers is sender authorisation *itself*, a forged
claim disabled the check that would have caught the forger.

**Why the lifecycle fix could not simply be repeated.** Moving the discriminator into the signed
payload — the 2026-07-29 remedy — is unavailable for two of the three: a publication's signed payload
**is** the canonical blueprint definition, so adding a property would change every publication id on
every register (CLAUDE.md §22); and genesis's payload is a pre-signed offline-ceremony artefact.
Authority is derivable from the signer's key, which is already signed, so nothing on the ledger moves.

**And signing the metadata would not have been enough anyway.** It makes a claim *attributable*, not
*authorised*: an attacker signing their own transaction produces a perfectly valid signature over
their own forged label.

**Fail closed.** If the anchor or roster cannot be consulted the exemption is withheld, in every
environment — no environment gate and no bypass flag. `ExemptionRefusalReason` distinguishes
`NotEntitled` ("you may not") from `AuthorityUnresolvable` ("I could not tell"), because those call
for different operator responses; both raise `sorcha_exemption_claim_refused_total` on the
`Sorcha.Validation` meter, dimensioned by kind, claim route and reason.

**Adding a kind means adding its rule.** `ExemptionKindCoverageTests` fails the build otherwise —
both defaults are wrong in different directions.

**What this does NOT change.** Not one of the six waivers is narrowed. Two are load-bearing for
governance quorum (F189 T054): approvals share a predecessor, a shape only the fork bypass permits,
and the chain-derived sender binding would otherwise treat the second approver as an impostor.

**Roster provisioning is part of this.** A register whose validator roster carries no active
`sorcha:blueprint-publish` entry accepts **no blueprint publications at all** — correct fail-closed
behaviour, and useless. Both roster-creating paths now provision one:
`RegisterCreationOrchestrator` for ordinary registers, and the genesis ceremony's
`BuildControlRecord` for the system register (it must go in the **signed payload**, which is what
`GovernanceRosterService` reconstructs from — not the genesis document's top-level
`validatorRoster` field).

**The two publish paths were unified onto `sorcha:blueprint-publish`.** They previously disagreed —
the per-register endpoint signed with `sorcha:register-control` while system-register seeding used
`sorcha:blueprint-publish` — so no single roster entry could have authorised both. Unifying cost
nothing only because the platform is pre-release and the estate could be wiped; after release it
would have needed a dual-accept transition.

⚠ **Adopting this needs a genesis re-ceremony and a re-genesis of every node**, because the system
register's roster lives inside the pre-signed genesis payload.

## gRPC Services

### gRPC access is TIERED, not blanket-authorized

`MapGrpcService<ValidatorGrpcService>()` is deliberately **not** behind
`.RequireAuthorization(...)`, even though every REST group in the same `Program.cs` is.

**Why a blanket gate would be wrong, not merely inconvenient.** Validator-to-validator consensus is
federated across *installations*, and Sorcha service tokens are installation-scoped by design
(Feature 136: issuer `urn:sorcha:{installation}`, audience `{installation}:service`, and bearer
validation deliberately **rejects** another installation's tokens). Requiring a token here would not
create a rolling-deploy window — it would **permanently sever consensus between installations**.
`PeerAuthInterceptor` already encodes the same conclusion: it validates a token when one is present,
otherwise lets the peer through "with lower trust" (FR-014), and sets `ValidateAudience = false`
precisely because "peer-to-peer traffic may not have audience set".

**What actually authenticates consensus is the payload, not the transport.** Votes, signatures and
dockets carry signatures verified against the **validator roster**: `ConsensusEngine
.CollectVoteFromValidatorAsync` resolves the voter via `IValidatorRegistry.GetValidatorAsync`, and
`DocketConfirmer` calls `IValidatorRegistry.IsRegisteredAsync`. The roster is installation-neutral,
which is exactly why it — not an installation-scoped JWT — is the right trust anchor. A forged vote
or docket from a stranger fails roster verification regardless of transport auth.

So the residual risk was never forgery; it was methods that mutate local state without a
roster-verified payload, plus resource exhaustion. `ValidatorGrpcAccessInterceptor` therefore
classifies the caller (a token if presented — an invalid or expired one degrades to anonymous rather
than failing the call — plus the Feature 175 mTLS node-identity thumbprint) and **enforces**
`ValidatorGrpcAccessPolicy`:

| RPC | Unauthenticated caller | Why |
|-----|------------------------|-----|
| `RequestVote` | ✅ allowed | vote signature resolved against the roster |
| `ValidateDocket` | ✅ allowed | read-only with respect to the chain |
| `ExchangeSignature` | ✅ allowed | collector rejects duplicate / invalid entries |
| `ReceiveConfirmedDocket` | ✅ allowed | initiator must be a registered validator |
| `GetHealthStatus` | ✅ allowed | liveness — a validator that cannot be probed cannot be federated with |
| `ReceiveTransaction` | ❌ refused | mempool ingest: no roster gate on admission, no cross-installation caller |
| *anything new* | ❌ refused | **fails closed** — private by default |

`ValidatorGrpcAccessPolicyTests` reflects over the generated `ValidatorServiceBase` and fails if any
RPC is unclassified **or** if the policy names an RPC that no longer exists — so adding an RPC to the
proto forces a deliberate access decision instead of silently publishing it.

**Known gap elsewhere:** `PeerAuthInterceptor` sets `IsAuthenticatedKey` and the node-identity
thumbprint, but **nothing in the repo consumes them** — the Peer service classifies callers and then
treats them all alike, so FR-014's "lower trust" half is inert there. Peer's RPCs need the same
enforcement step this service now has.

### Proto Definition Location
`specs/002-validator-service/contracts/validator.proto`

### RequestVote RPC
Called by a peer validator proposing a new docket for consensus.

**Request:**
```protobuf
message VoteRequest {
  string docket_id = 1;
  string register_id = 2;
  int32 docket_number = 3;
  string docket_hash = 4;
  string previous_hash = 5;
  google.protobuf.Timestamp created_at = 6;
  repeated Transaction transactions = 7;
  string proposer_validator_id = 8;
  Signature proposer_signature = 9;
  string merkle_root = 10;
}
```

**Response:**
```protobuf
message VoteResponse {
  string vote_id = 1;
  VoteDecision decision = 2; // APPROVE or REJECT
  string rejection_reason = 3;
  string validator_id = 4;
  google.protobuf.Timestamp voted_at = 5;
  Signature validator_signature = 6;
}
```

### ValidateDocket RPC
Called when a peer broadcasts a confirmed docket for validation.

**Request:**
```protobuf
message DocketValidationRequest {
  string docket_id = 1;
  string register_id = 2;
  int32 docket_number = 3;
  string docket_hash = 4;
  string previous_hash = 5;
  google.protobuf.Timestamp created_at = 6;
  repeated Transaction transactions = 7;
  string proposer_validator_id = 8;
  Signature proposer_signature = 9;
  string merkle_root = 10;
  repeated ConsensusVote votes = 11;
}
```

**Response:**
```protobuf
message DocketValidationResponse {
  bool is_valid = 1;
  bool should_persist = 2;
  bool is_fork = 3;
  repeated string validation_errors = 4;
}
```

### GetHealthStatus RPC
Returns validator health and status information.

**Request:** Empty

**Response:**
```protobuf
message HealthStatusResponse {
  HealthStatus status = 1; // HEALTHY, DEGRADED, UNHEALTHY
  string validator_id = 2;
  int32 active_registers = 3;
  google.protobuf.Timestamp last_heartbeat = 4;
}
```

---

## Components

### ValidatorOrchestrator
**Purpose:** Coordinates the complete validation pipeline for all registers.

**Key Methods:**
- `StartValidatorAsync(registerId)` - Activates validation for a register
- `StopValidatorAsync(registerId, persistMemPool)` - Gracefully stops validation
- `GetValidatorStatusAsync(registerId)` - Retrieves current validator state
- `ProcessValidationPipelineAsync(registerId)` - Executes a single pipeline iteration

**Pipeline Steps:**
1. Check docket build triggers (time-based OR size-based)
2. Build docket from memory pool via `DocketBuilder`
3. Achieve consensus via `ConsensusEngine`
4. Write confirmed docket to Register Service
5. Cleanup processed transactions from memory pool

### DocketBuilder
**Purpose:** Builds cryptographically-sealed dockets from pending transactions.

**Key Features:**
- Genesis docket creation for new registers
- Merkle tree computation for transaction integrity
- SHA-256 docket hashing with previous hash linkage
- Signature creation via Wallet Service integration
- Configurable transaction limits per docket

**Configuration:**
- `MaxTransactionsPerDocket` (default: 100)
- `TimeBasedTriggerInterval` (default: 60 seconds)
- `SizeBasedTriggerCount` (default: 50 transactions)
- `AllowEmptyDockets` (default: false)

### ConsensusEngine
**Purpose:** Coordinates distributed consensus across validator nodes.

**Algorithm:**
1. Publish proposed docket to peer network (via Peer Service)
2. Query active validators for the register
3. Collect votes in parallel using gRPC `RequestVote` RPCs
4. Apply timeout for non-responsive validators
5. Calculate approval percentage (votes_approve / total_votes)
6. Achieve consensus if percentage >= threshold (default: >50%)

**Configuration:**
- `ConsensusThreshold` (default: 0.51 = >50%)
- `VoteTimeout` (default: 30 seconds)
- `MinimumValidators` (default: 1)

### MemPoolManager
**Purpose:** Thread-safe management of pending transactions with priority queuing.

**Key Features:**
- Per-register memory pools (isolated transaction spaces)
- Priority queues: High (top priority) > Normal (FIFO) > Low (best effort)
- Automatic eviction (oldest low/normal priority transactions)
- Capacity management with configurable limits
- High-priority quota protection (default: 20% of pool)

**Configuration:**
- `MaxSize` (default: 1000 transactions per register)
- `HighPriorityQuota` (default: 0.2 = 20%)
- `ExpirationCheckInterval` (default: 60 seconds)

### GenesisManager
**Purpose:** Creates genesis dockets (first blocks) for new registers.

**Genesis Docket Properties:**
- `DocketNumber` = 0
- `PreviousHash` = null (no predecessor)
- `IsGenesis` = true
- Special validation rules (no previous hash required)

### Sealing pipeline & idle-stall resilience (issue #814)

Sealing runs as a **two-stage pipeline**, each a `BackgroundService` loop over the Redis-backed
`RegisterMonitoringRegistry` (`GetAll()`):

1. **Validation** — `ValidationEngineService` polls each monitored register's **unverified pool**,
   validates, and enqueues to the **verified queue**.
2. **Docket build** — `DocketBuildTriggerService` drains the verified queue and seals a docket.

**The failure mode (#814):** after a long idle a keep-alive connection (Redis or the wallet-service
gRPC channel) goes stale. An `await` inside `ValidationEngineService.ProcessRegisterAsync` then *hangs*
instead of throwing; the in-memory "already processing" flag (released only in a `finally`) is never
released, so every later batch **skips that register forever** — the validator stays healthy but stops
sealing. A restart recovered only because it cleared the in-memory flag and ran the startup pool drain.

**The guards now in place:**
- Each per-register cycle (both stages) is bounded by a linked `CancellationTokenSource`
  (`ValidationTimeout` / 30 s), so a stale connection **throws** (caught → loop continues → the flag
  is released) rather than hanging.
- `_activeRegisters` is keyed by start time; a slot held past `StuckReclaimAfter`
  (`max(3× ValidationTimeout, 90 s)`) is **reclaimed** — belt-and-braces for an await that ignores
  cancellation and outlives its timeout.
- The Redis multiplexer uses `KeepAlive` PINGs, `AbortOnConnectFail=false` + `ConnectRetry`
  auto-reconnect, and explicit sync/async timeouts so a dead socket surfaces as a throw.

**Observability** — the condition is no longer silent. Watch these counters
(`Sorcha.Validator.Mempool` meter): `sorcha_validator_validation_cycle_timeout_total` (a cycle hit the
stale-connection guard) and `sorcha_validator_validation_slot_reclaimed_total` (a stuck slot was
reclaimed — any non-zero value means the timeout guard was itself bypassed and needs investigation).

---

## Configuration

### appsettings.json

```json
{
  "Validator": {
    "ValidatorId": "validator-001",
    "ValidatorWalletAddress": "0x1234567890abcdef1234567890abcdef12345678"
  },
  "Consensus": {
    "ConsensusThreshold": 0.51,
    "VoteTimeout": "00:00:30",
    "MinimumValidators": 1
  },
  "MemPool": {
    "MaxSize": 1000,
    "HighPriorityQuota": 0.2,
    "ExpirationCheckInterval": "00:01:00"
  },
  "DocketBuild": {
    "MaxTransactionsPerDocket": 100,
    "TimeBasedTriggerInterval": "00:01:00",
    "SizeBasedTriggerCount": 50,
    "AllowEmptyDockets": false
  },
  "ServiceClients": {
    "WalletService": "https://localhost:7084",
    "RegisterService": "https://localhost:7085",
    "PeerService": "https://localhost:7086"
  }
}
```

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment (Development/Production) | Development |
| `ASPNETCORE_URLS` | Service listening URLs | https://localhost:7087 |
| `VALIDATOR_ID` | Unique validator identifier | validator-001 |
| `WALLET_SERVICE_URL` | Wallet Service endpoint | https://localhost:7084 |
| `REGISTER_SERVICE_URL` | Register Service endpoint | https://localhost:7085 |
| `PEER_SERVICE_URL` | Peer Service endpoint | https://localhost:7086 |

---

## Data Models

### Docket
Represents a block in the blockchain.

```csharp
public class Docket
{
    public required string DocketId { get; init; }
    public required string RegisterId { get; init; }
    public required int DocketNumber { get; init; }
    public required string DocketHash { get; init; }
    public string? PreviousHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required List<Transaction> Transactions { get; init; }
    public required DocketStatus Status { get; init; }
    public required string ProposerValidatorId { get; init; }
    public required Signature ProposerSignature { get; init; }
    public required string MerkleRoot { get; init; }
    public List<ConsensusVote> Votes { get; init; } = new();
}
```

### Transaction
Represents a validated action execution.

```csharp
public class Transaction
{
    public required string TransactionId { get; init; }
    public required string RegisterId { get; init; }
    public required string BlueprintId { get; init; }
    public required string ActionId { get; init; }
    public required JsonElement Payload { get; init; }
    public required string PayloadHash { get; init; }
    public required List<Signature> Signatures { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public TransactionPriority Priority { get; init; } = TransactionPriority.Normal;
    public Dictionary<string, string>? Metadata { get; init; }
    public DateTimeOffset? AddedToPoolAt { get; set; }
}
```

### ConsensusVote
Represents a validator's vote on a proposed docket.

```csharp
public class ConsensusVote
{
    public required string VoteId { get; init; }
    public required string DocketId { get; init; }
    public required string ValidatorId { get; init; }
    public required VoteDecision Decision { get; init; }
    public string? RejectionReason { get; init; }
    public required DateTimeOffset VotedAt { get; init; }
    public required Signature ValidatorSignature { get; init; }
    public required string DocketHash { get; init; }
}
```

### Enumerations

**DocketStatus:**
- `Proposed` - Awaiting consensus
- `Confirmed` - Consensus achieved
- `Rejected` - Consensus failed
- `Persisted` - Written to Register Service

**VoteDecision:**
- `Approve` - Docket is valid
- `Reject` - Docket is invalid

**TransactionPriority:**
- `High` - Urgent, top priority
- `Normal` - Standard FIFO processing
- `Low` - Best effort, can be evicted

---

## Testing

### Test Coverage

| Component | Unit Tests | Integration Tests | Coverage |
|-----------|-----------|------------------|----------|
| **Sorcha.Validator.Core** | 6 files | N/A | ~90% |
| **Sorcha.Validator.Service** | 10 files | Included | ~75% |
| **Overall** | **16 test files** | **Comprehensive** | **~80%** |

### Core Library Tests
**Location:** `tests/Sorcha.Validator.Core.Tests/`

- `DocketValidatorTests.cs` - Docket structure validation, hash computation
- `TransactionValidatorTests.cs` - Transaction structure and schema validation
- `ConsensusValidatorTests.cs` - Consensus vote validation

### Service Tests
**Location:** `tests/Sorcha.Validator.Service.Tests/`

- Validator orchestrator lifecycle tests
- Docket building workflow tests
- Consensus engine vote collection tests
- Memory pool management tests
- Admin endpoint integration tests

### Running Tests

```bash
# Run all Validator Service tests
dotnet test tests/Sorcha.Validator.Core.Tests
dotnet test tests/Sorcha.Validator.Service.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter "FullyQualifiedName~DocketBuilderTests"

# Watch mode (auto-rerun on changes)
dotnet watch test --project tests/Sorcha.Validator.Service.Tests
```

---

## Deployment

### .NET Aspire Integration

The Validator Service is integrated with .NET Aspire for orchestration:

**AppHost Configuration:**
```csharp
var validatorService = builder.AddProject<Projects.Sorcha_Validator_Service>("validator-service")
    .WithReference(redis)
    .WithEnvironment("VALIDATOR_ID", "validator-001");

// API Gateway routes
builder.AddProject<Projects.Sorcha_ApiGateway>("api-gateway")
    .WithReference(validatorService);
```

### Docker Deployment

**Dockerfile:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Sorcha.Validator.Service/Sorcha.Validator.Service.csproj", "src/Services/Sorcha.Validator.Service/"]
RUN dotnet restore
COPY . .
WORKDIR "/src/src/Services/Sorcha.Validator.Service"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sorcha.Validator.Service.dll"]
```

**Build and Run:**
```bash
# Build Docker image
docker build -t sorcha-validator-service:latest -f src/Services/Sorcha.Validator.Service/Dockerfile .

# Run container
docker run -d \
  -p 8087:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e VALIDATOR_ID=validator-001 \
  -e WALLET_SERVICE_URL=https://wallet-service:8080 \
  -e REGISTER_SERVICE_URL=https://register-service:8080 \
  -e PEER_SERVICE_URL=https://peer-service:8080 \
  sorcha-validator-service:latest
```

### Health Checks

- **Liveness:** `GET /alive`
- **Readiness:** `GET /health`

Health checks verify:
- Service is running
- Redis connectivity (if configured)
- Wallet Service reachable
- Register Service reachable
- Peer Service reachable

---

## Development

### Prerequisites

- .NET 10.0 SDK
- Redis (for distributed caching)
- Wallet Service (running on https://localhost:7084)
- Register Service (running on https://localhost:7085)
- Peer Service (running on https://localhost:7086)

### Local Development

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run service
dotnet run --project src/Services/Sorcha.Validator.Service

# Run with .NET Aspire (recommended)
dotnet run --project src/Apps/Sorcha.AppHost

# Access Aspire Dashboard
open https://localhost:15888

# Access API documentation
open https://localhost:7087/scalar/v1
```

### Project Structure

```
src/Services/Sorcha.Validator.Service/
├── Program.cs                      # Entry point, DI configuration
├── appsettings.json               # Configuration
├── appsettings.Development.json   # Development overrides
├── Endpoints/
│   ├── ValidationEndpoints.cs     # Transaction validation APIs
│   └── AdminEndpoints.cs          # Admin control APIs
├── GrpcServices/
│   └── ValidatorGrpcService.cs    # gRPC peer communication
├── Services/
│   ├── ValidatorOrchestrator.cs   # Pipeline coordinator
│   ├── DocketBuilder.cs           # Docket construction
│   ├── ConsensusEngine.cs         # Consensus coordination
│   ├── MemPoolManager.cs          # Transaction memory pool
│   ├── GenesisManager.cs          # Genesis docket creation
│   ├── MemPoolCleanupService.cs   # Background cleanup
│   └── DocketBuildTriggerService.cs  # Background builder
├── Configuration/
│   ├── ValidatorConfiguration.cs
│   ├── ConsensusConfiguration.cs
│   ├── MemPoolConfiguration.cs
│   └── DocketBuildConfiguration.cs
├── Models/
│   ├── Docket.cs
│   ├── Transaction.cs
│   ├── ConsensusVote.cs
│   ├── Signature.cs
│   ├── DocketStatus.cs (enum)
│   ├── VoteDecision.cs (enum)
│   └── TransactionPriority.cs (enum)
├── Managers/
│   └── DocketManager.cs
├── Validators/
│   └── ChainValidator.cs
└── Middleware/

src/Common/Sorcha.Validator.Core/
├── Validators/
│   ├── DocketValidator.cs         # Pure docket validation logic
│   ├── TransactionValidator.cs    # Pure transaction validation
│   └── ConsensusValidator.cs      # Pure consensus validation
└── Models/
    ├── ValidationResult.cs
    └── ValidationError.cs
```

### Adding New Validators

1. Create validator in `Sorcha.Validator.Core/Validators/`
2. Keep logic pure (no I/O, no network calls)
3. Add comprehensive unit tests
4. Register in `Program.cs` DI container
5. Integrate with `ValidatorOrchestrator` or `ConsensusEngine`

### Debugging Tips

- Use Aspire Dashboard for distributed tracing
- Check memory pool stats via `/api/v1/transactions/mempool/{registerId}`
- Manual pipeline execution via `/api/admin/validators/{registerId}/process`
- Enable debug logging: `"Logging": { "LogLevel": { "Sorcha.Validator": "Debug" } }`

---

## Documentation

### Related Documents

- **Specification:** [.specify/specs/sorcha-validator-service.md](https://github.com/Sorcha-Platform/Sorcha/blob/master/.specify/specs/sorcha-validator-service.md)
- **Design:** [docs/validator-service-design.md](../../../docs/reference/validator-service-design.md)
- **Architecture:** [docs/architecture.md](../../../docs/architecture.md)
- **API Documentation:** [https://localhost:7087/scalar/v1](https://localhost:7087/scalar/v1) (when running)

### Quick Links

- **GitHub Repository:** [https://github.com/Sorcha-Platform/Sorcha](https://github.com/Sorcha-Platform/Sorcha)
- **Issue Tracker:** [https://github.com/Sorcha-Platform/Sorcha/issues](https://github.com/Sorcha-Platform/Sorcha/issues)
- **Spec-Kit Guide:** [.specify/README.md](https://github.com/Sorcha-Platform/Sorcha/blob/master/.specify/README.md)

---

## Support and Contributing

### Getting Help

- Review [TROUBLESHOOTING.md](../../../docs/guides/TROUBLESHOOTING.md) for common issues
- Check [CLAUDE.md](https://github.com/Sorcha-Platform/Sorcha/blob/master/CLAUDE.md) for AI assistant guidelines
- Create a GitHub issue with the `validator-service` label

### Contributing

- Follow [CONTRIBUTING.md](https://github.com/Sorcha-Platform/Sorcha/blob/master/CONTRIBUTING.md) guidelines
- Run tests before submitting: `dotnet test`
- Update documentation for API changes
- Follow [constitutional principles](https://github.com/Sorcha-Platform/Sorcha/blob/master/.specify/constitution.md)

---

**Version:** 1.0
**Last Updated:** 2026-03-01
**Status:** Complete (100% MVD)
**Owner:** Sorcha Architecture Team
