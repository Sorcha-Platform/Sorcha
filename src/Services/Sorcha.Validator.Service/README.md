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

## gRPC Services

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

- **Specification:** [.specify/specs/sorcha-validator-service.md](./../../../.specify/specs/sorcha-validator-service.md)
- **Design:** [docs/validator-service-design.md](./../../../docs/validator-service-design.md)
- **Implementation Plan:** [docs/validator-service-implementation-plan.md](./../../../docs/validator-service-implementation-plan.md)
- **Architecture:** [docs/architecture.md](./../../../docs/architecture.md)
- **API Documentation:** [https://localhost:7087/scalar/v1](https://localhost:7087/scalar/v1) (when running)

### Quick Links

- **GitHub Repository:** [https://github.com/sorcha-platform/sorcha](https://github.com/sorcha-platform/sorcha)
- **Issue Tracker:** [https://github.com/sorcha-platform/sorcha/issues](https://github.com/sorcha-platform/sorcha/issues)
- **Spec-Kit Guide:** [.specify/README.md](./../../../.specify/README.md)

---

## Support and Contributing

### Getting Help

- Review [TROUBLESHOOTING.md](./../../../TROUBLESHOOTING.md) for common issues
- Check [CLAUDE.md](./../../../CLAUDE.md) for AI assistant guidelines
- Create a GitHub issue with the `validator-service` label

### Contributing

- Follow [CONTRIBUTING.md](./../../../CONTRIBUTING.md) guidelines
- Run tests before submitting: `dotnet test`
- Update documentation for API changes
- Follow [constitutional principles](./../../../.specify/constitution.md)

---

**Version:** 1.0
**Last Updated:** 2026-03-01
**Status:** Complete (100% MVD)
**Owner:** Sorcha Architecture Team
