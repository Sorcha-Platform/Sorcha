# Implementation Plan: Validator Service - Distributed Transaction Validation and Consensus

**Branch**: `002-validator-service` | **Date**: 2025-12-22 | **Spec**: [spec.md](./spec.md)

## Summary

The Validator Service is a critical component of the Sorcha decentralised register platform, responsible for validating transactions, building dockets (blocks), achieving distributed consensus, and maintaining blockchain integrity. It receives unvalidated transactions from the Peer Service, validates them against blueprint rules, creates proposed dockets using hybrid triggering (time OR size threshold), coordinates consensus voting across validator instances, and persists confirmed dockets to the Register Service. The service implements longest-chain fork resolution, FIFO-with-priority memory pool management, and integrates with Peer Service reputation scoring to isolate malicious validators.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**:
- .NET Aspire 13.0.0 (orchestration)
- Grpc.Net 2.71.0 (inter-service communication)
- FluentValidation 11.10.0 (input validation)
- OpenTelemetry 1.12.0 (observability)
- Sorcha.Blueprint.Models (blueprint validation library - existing)
- Sorcha.Cryptography (signature operations - existing)
- Sorcha.TransactionHandler (transaction building - existing)
- Sorcha.ServiceDefaults (health checks, telemetry - existing)

**Storage**:
- In-memory data structures for memory pools (per-register ConcurrentQueue with priority queues)
- Optional Redis for distributed coordination and pub/sub (if multiple validator instances)
- Memory pool persistence (implementation TBD - could use Redis, EF Core, or file-based)

**Testing**: xUnit with FluentAssertions, Moq for mocking, Testcontainers for integration tests

**Target Platform**: Linux/Windows server, containerized via Docker, orchestrated by .NET Aspire

**Project Type**: Microservice (ASP.NET Core Minimal APIs + gRPC)

**Performance Goals**:
- 500ms transaction validation (P95)
- 100 transactions/second per register throughput
- 30-second consensus completion (3-10 validators, P95)
- 10,000 pending transactions per memory pool

**Constraints**:
- Must use gRPC for all inter-service communication (per constitution)
- Write-only access to Register Service (security boundary)
- System wallet per validator instance (managed by Wallet Service)
- Network latency <5 seconds RTT between validators (assumption)

**Scale/Scope**:
- Multiple validator instances per deployment
- Multiple independent registers (multi-tenant isolation)
- 3-10 validators per register initially (MVP)
- Horizontal scalability

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Microservices-First Architecture ✅
- **Status**: PASS
- **Compliance**: Validator Service is independently deployable microservice, uses .NET Aspire orchestration, minimal coupling (communicates via gRPC)
- **Dependencies**: Downward only - depends on Peer, Wallet, Register, Blueprint services; no upward dependencies

### Security First ✅
- **Status**: PASS
- **Compliance**:
  - Zero trust model: validates all incoming transactions and dockets
  - System wallet managed by Wallet Service (no private keys in Validator)
  - Input validation using FluentValidation on all external boundaries
  - Reputation scoring isolates malicious validators
  - All service communication via gRPC (encrypted)

### API Documentation ✅
- **Status**: PASS
- **Compliance**: Will use .NET 10 built-in OpenAPI, Scalar.AspNetCore for docs, XML comments for all public APIs

### Testing Requirements ⚠️
- **Status**: CONDITIONAL PASS
- **Compliance**: Will target >85% coverage for new code (exceeds 80% minimum)
- **Note**: Existing Validator Service has partial implementation; must ensure tests cover all new consensus/memory pool logic

### Code Quality ✅
- **Status**: PASS
- **Compliance**: C# 13, .NET 10, async/await for I/O, dependency injection, nullable reference types enabled

### Blueprint Creation Standards ✅
- **Status**: PASS (N/A)
- **Compliance**: Validator consumes blueprints (doesn't create them); uses existing validation library

### Domain-Driven Design ✅
- **Status**: PASS
- **Compliance**: Uses ubiquitous language (Docket not "block", Transaction not "item", Blueprint validation)

### Observability by Default ✅
- **Status**: PASS
- **Compliance**: OpenTelemetry integration via ServiceDefaults, structured logging, health check endpoints (/health, /alive), metrics for memory pool size, throughput, consensus success rate

### **Gate Decision: PASS** ✅
All constitutional requirements met. Proceed to Phase 0 research.

## Project Structure

### Documentation (this feature)

```text
specs/002-validator-service/
├── plan.md              # This file
├── research.md          # Phase 0 output (technology decisions, patterns)
├── data-model.md        # Phase 1 output (entities, state machines)
├── quickstart.md        # Phase 1 output (developer guide)
├── contracts/           # Phase 1 output (gRPC protos, OpenAPI specs)
│   ├── validator.proto  # gRPC service definition
│   └── openapi.json     # REST API spec
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

**Structure Decision**: Microservice architecture with existing Sorcha service patterns

```text
src/
├── Services/
│   └── Sorcha.Validator.Service/           # Main service (EXISTING - partial impl)
│       ├── Program.cs                       # Entry point, DI setup
│       ├── appsettings.json                # Configuration
│       ├── Endpoints/                       # Minimal API endpoints
│       │   ├── ValidationEndpoints.cs       # Transaction/docket validation
│       │   ├── AdminEndpoints.cs            # Start/stop/status
│       │   └── MetricsEndpoints.cs          # Observability
│       ├── Services/                        # Business logic orchestration
│       │   ├── IValidatorOrchestrator.cs
│       │   ├── ValidatorOrchestrator.cs     # Main coordinator
│       │   ├── IDocketBuilder.cs
│       │   ├── DocketBuilder.cs             # Builds dockets from memory pool
│       │   ├── ITransactionValidator.cs
│       │   ├── TransactionValidator.cs      # Validates incoming transactions
│       │   ├── IConsensusEngine.cs
│       │   ├── ConsensusEngine.cs           # Coordinates voting
│       │   ├── IMemPoolManager.cs
│       │   ├── MemPoolManager.cs            # Manages memory pools (FIFO+priority)
│       │   └── IGenesisManager.cs
│       │   └── GenesisManager.cs            # Handles genesis dockets
│       ├── GrpcServices/                    # gRPC service implementations
│       │   └── ValidatorGrpcService.cs      # gRPC endpoints for peer communication
│       ├── Clients/                         # gRPC clients for external services
│       │   ├── WalletServiceClient.cs       # Signature operations
│       │   ├── PeerServiceClient.cs         # Docket distribution, discovery
│       │   ├── RegisterServiceClient.cs     # Read/write dockets
│       │   └── BlueprintServiceClient.cs    # Retrieve blueprints
│       ├── Models/                          # DTOs and domain models
│       │   ├── Transaction.cs               # Transaction representation
│       │   ├── Docket.cs                    # Docket (block) model
│       │   ├── ConsensusVote.cs             # Vote structure
│       │   ├── GenesisConfig.cs             # Genesis configuration
│       │   ├── ValidationResult.cs          # Validation outcomes
│       │   ├── DocketBuildResult.cs         # Docket building result
│       │   ├── ConsensusResult.cs           # Consensus outcome
│       │   └── MemPoolStats.cs              # Memory pool metrics
│       ├── Configuration/                   # Configuration models
│       │   ├── ValidatorConfiguration.cs    # Main config
│       │   ├── ConsensusConfiguration.cs    # Consensus settings
│       │   └── MemPoolConfiguration.cs      # Memory pool settings
│       ├── Managers/                        # EXISTING - Domain logic managers
│       │   └── DocketManager.cs             # EXISTING - may refactor into services/
│       ├── Validators/                      # EXISTING - Validation logic
│       │   └── ChainValidator.cs            # EXISTING - chain integrity checks
│       └── Middleware/                      # HTTP middleware
│           └── RateLimitingMiddleware.cs    # DoS protection
│
├── Common/
│   ├── Sorcha.Validator.Core/              # NEW - Pure validation logic (enclave-safe)
│   │   ├── Validators/                      # Stateless validation functions
│   │   │   ├── DocketValidator.cs           # Docket structure/hash validation
│   │   │   ├── TransactionValidator.cs      # Transaction validation
│   │   │   ├── ConsensusValidator.cs        # Vote validation
│   │   │   └── ChainValidator.cs            # Fork resolution, chain integrity
│   │   ├── Models/                          # Validation models
│   │   │   ├── ValidationResult.cs
│   │   │   ├── ValidationError.cs
│   │   │   └── ValidationRules.cs
│   │   └── Cryptography/
│   │       └── HashingUtilities.cs          # SHA256 hashing for dockets
│   │
│   └── [EXISTING LIBRARIES]
│       ├── Sorcha.Blueprint.Models/         # Blueprint domain models
│       ├── Sorcha.Cryptography/             # Signature operations
│       ├── Sorcha.TransactionHandler/       # Transaction building
│       ├── Sorcha.Register.Models/          # Register/Docket models
│       └── Sorcha.ServiceDefaults/          # Aspire defaults
│
tests/
├── Sorcha.Validator.Core.Tests/            # NEW - Unit tests for core library
│   ├── DocketValidatorTests.cs
│   ├── TransactionValidatorTests.cs
│   ├── ConsensusValidatorTests.cs
│   └── ChainValidatorTests.cs
│
└── Sorcha.Validator.Service.Tests/         # Service integration tests
    ├── Integration/
    │   ├── DocketBuildingTests.cs
    │   ├── ValidationEndpointsTests.cs
    │   ├── ConsensusTests.cs
    │   └── AdminEndpointsTests.cs
    └── Unit/
        ├── DocketBuilderTests.cs
        ├── TransactionValidatorTests.cs
        ├── ConsensusEngineTests.cs
        └── MemPoolManagerTests.cs
```

**Integration with Existing Code:**
- **Sorcha.Validator.Service** directory already exists with partial implementation (DocketManager, ChainValidator)
- Will refactor existing code into new service structure (Managers/ → Services/)
- Will extract pure validation logic into new Sorcha.Validator.Core library (enclave-safe, no I/O)
- Will leverage existing Sorcha.Blueprint.Models validation library (FR-003)
- Will use existing Sorcha.Cryptography for signature operations (FR-016)
- Will use existing Sorcha.Register.Models for Docket/Transaction models

## Complexity Tracking

**No Constitutional Violations** - No justification required. The design follows all constitutional principles:
- Single microservice (not adding unnecessary projects)
- Uses standard .NET Aspire patterns
- Follows existing Sorcha service structure
- Separates pure domain logic (Core library) from service orchestration (Service project)

## Phase 0: Research & Technology Decisions

**Status**: Ready to execute

### Research Areas Identified

Based on Technical Context analysis, the following areas require research to resolve "NEEDS CLARIFICATION" items:

1. **Memory Pool Persistence Strategy**
   - **Question**: How should memory pools persist state across service restarts?
   - **Options**: Redis (distributed), EF Core + SQL (durable), File-based (simple), In-memory only (ephemeral)
   - **Criteria**: Durability, performance, complexity, multi-instance support

2. **Consensus Coordination Mechanism**
   - **Question**: How should validators coordinate vote collection?
   - **Options**: gRPC streaming, Peer Service pub/sub (Redis), Direct gRPC calls, Polling
   - **Criteria**: Latency, reliability, complexity, scalability

3. **Fork Resolution Implementation**
   - **Question**: How deep should longest-chain resolution go?
   - **Options**: Unlimited depth (full reorg), Limited depth (e.g., 10 dockets), Recent only (last 3 dockets)
   - **Criteria**: Security, performance, complexity, edge case handling

4. **Memory Pool Priority Scheme**
   - **Question**: How are transaction priorities defined and assigned?
   - **Options**: Explicit field in transaction, Derived from blueprint action, Gas fee-based, Timestamp-based
   - **Criteria**: Fairness, gaming resistance, simplicity

5. **Hybrid Docket Trigger Implementation**
   - **Question**: How to efficiently implement time OR size threshold triggering?
   - **Options**: Timer + queue size check, Background service with polling, Event-driven architecture
   - **Criteria**: Accuracy, resource usage, testability

### Research Tasks (will be dispatched to agents)

These will be consolidated into `research.md`:

1. Research memory pool persistence patterns for blockchain systems
2. Research consensus coordination mechanisms in distributed systems
3. Research longest-chain fork resolution strategies and depth limits
4. Research transaction priority schemes in blockchain memory pools
5. Research hybrid event triggering patterns (time OR condition)

**Next Step**: Generate research.md with findings

## Phase 1: Design Artifacts

**Status**: Pending (awaits Phase 0 completion)

Will generate:
- `data-model.md` - Entities, state machines, validation rules
- `contracts/validator.proto` - gRPC service definitions
- `contracts/openapi.json` - REST API specification
- `quickstart.md` - Developer getting started guide

## Phase 2: Task Breakdown

**Status**: Out of scope for `/speckit.plan` - will be generated by `/speckit.tasks` command

---

**Plan Status**: Phase 0 research pending. Run research agents to resolve Technical Context unknowns, then proceed to Phase 1 design.
