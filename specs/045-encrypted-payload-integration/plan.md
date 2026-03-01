# Implementation Plan: Encrypted Payload Integration

**Branch**: `045-encrypted-payload-integration` | **Date**: 2026-03-01 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/045-encrypted-payload-integration/spec.md`

## Summary

Wire envelope encryption into the action transaction pipeline so that all payload data is encrypted before register storage. Recipients decrypt only the fields they're entitled to see. The approach uses disclosure group optimization (encrypt once per unique field set, wrap key per recipient), in-process symmetric crypto (XChaCha20-Poly1305 default), async background processing with SignalR progress feedback, and fills algorithm gaps (P-256 ECIES, ML-KEM-768 decapsulate fix). Transaction size enforcement raised to 4MB with actual enforcement in the Validator.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Sorcha.Cryptography (libsodium, .NET System.Security.Cryptography), Sorcha.TransactionHandler (PayloadManager), SignalR, System.Threading.Channels (new to codebase)
**Storage**: MongoDB (encrypted transactions), Redis (mempool/caching), PostgreSQL (participants/wallets via Tenant Service)
**Testing**: xUnit + FluentAssertions + Moq, >85% coverage for new code
**Target Platform**: Linux containers (Docker), Windows development
**Project Type**: Microservices — changes span 5 services + 2 shared libraries
**Performance Goals**: SC-004: 2s encryption for 5 recipients/3 groups/10KB payload; SC-005: 500ms first SignalR notification after HTTP 202
**Constraints**: 4MB max transaction size, HTTP 10MB (InputValidationMiddleware), gRPC 16MB, MongoDB 16MB BSON; RSA-4096 wraps max 446 bytes (fine for 32-byte symmetric keys)
**Scale/Scope**: Typical actions 5-50 recipients; up to 100+ for large workflows; ~20 files modified, ~10 new files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes span Blueprint, Register, Wallet, Validator, Cryptography — each remains independently deployable. No new upward dependencies. |
| II. Security First | PASS | This IS the security improvement — encrypting payload data at rest with AES-256-GCM / XChaCha20-Poly1305. Zero trust: recipients must have private key to read. |
| III. API Documentation | PASS | All new endpoints will have .WithSummary()/.WithDescription(), XML docs on public APIs. Contracts defined in contracts/. |
| IV. Testing Requirements | PASS | Unit tests for all new code. Integration tests for round-trip encrypt/decrypt across all 4 algorithms. Target >85% coverage. |
| V. Code Quality | PASS | Async/await throughout. DI for all services. Nullable enabled. No compiler warnings. |
| VI. Blueprint Creation | N/A | No blueprint format changes. |
| VII. DDD | PASS | Uses ubiquitous language: Disclosure (not "visibility"), Participant (not "user"), Action (not "step"). |
| VIII. Observability | PASS | OpenTelemetry traces for encryption pipeline steps. Structured logging with ILogger. |

**Post-Phase 1 Re-check**: No violations introduced. Channel<T> is new to the codebase but is standard .NET — simpler than Redis-based queueing for in-process work. All new entities use existing patterns (IOptions, BackgroundService, IHubContext).

## Project Structure

### Documentation (this feature)

```text
specs/045-encrypted-payload-integration/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 research findings
├── data-model.md        # Phase 1 data model
├── quickstart.md        # Phase 1 quickstart guide
├── contracts/           # Phase 1 API contracts
│   ├── register-batch-public-keys.yaml
│   ├── blueprint-action-encrypted.yaml
│   ├── signalr-encryption-events.yaml
│   └── validator-size-enforcement.yaml
├── checklists/
│   └── requirements.md  # Quality checklist (from /speckit.specify)
└── tasks.md             # Phase 2 output (from /speckit.tasks — not yet created)
```

### Source Code (repository root)

```text
src/
├── Common/
│   ├── Sorcha.Cryptography/
│   │   └── Core/
│   │       └── CryptoModule.cs              # MODIFY: P-256 ECIES encrypt/decrypt (lines 627-641)
│   ├── Sorcha.TransactionHandler/
│   │   ├── Payload/
│   │   │   └── PayloadManager.cs            # EXISTING: Envelope encryption (already works)
│   │   └── Encryption/
│   │       ├── DisclosureGroupBuilder.cs     # NEW: Group recipients by disclosure field sets
│   │       ├── EncryptionPipelineService.cs  # NEW: Orchestrate encrypt + wrap + assemble
│   │       └── Models/                       # NEW: EncryptedPayloadGroup, WrappedKey, etc.
│   └── Sorcha.ServiceClients/
│       └── Register/
│           ├── IRegisterServiceClient.cs     # MODIFY: Add batch public key method
│           └── RegisterServiceClient.cs      # MODIFY: Implement batch public key method
├── Services/
│   ├── Sorcha.Blueprint.Service/
│   │   ├── Services/
│   │   │   ├── Implementation/
│   │   │   │   ├── ActionExecutionService.cs # MODIFY: Wire encryption, return HTTP 202
│   │   │   │   ├── EncryptionBackgroundService.cs  # NEW: Channel consumer, async pipeline
│   │   │   │   └── NotificationService.cs    # MODIFY: Add encryption progress events
│   │   │   └── Interfaces/
│   │   │       ├── ITransactionBuilderService.cs  # MODIFY: New encrypted transaction builder
│   │   │       └── IEncryptionOperationStore.cs   # NEW: Track operation status
│   │   ├── Hubs/
│   │   │   └── ActionsHub.cs                 # EXISTING: Add client event types (no hub changes needed)
│   │   ├── Models/
│   │   │   └── EncryptionOperationModels.cs  # NEW: DTOs for operation tracking
│   │   └── Program.cs                        # MODIFY: Register new services
│   ├── Sorcha.Register.Service/
│   │   └── Program.cs                        # MODIFY: Add batch public key endpoint
│   ├── Sorcha.Validator.Service/
│   │   ├── Services/
│   │   │   └── TransactionReceiver.cs        # MODIFY: Enforce MaxTransactionSizeBytes
│   │   └── Configuration/
│   │       └── TransactionReceiverConfiguration.cs  # MODIFY: Default 1MB → 4MB
│   └── Sorcha.Wallet.Service/
│       └── Endpoints/
│           └── WalletEndpoints.cs            # MODIFY: Fix ML-KEM-768 decapsulate (line 1321)
tests/
├── Sorcha.Cryptography.Tests/
│   └── Core/
│       └── CryptoModuleNistP256Tests.cs      # NEW: P-256 ECIES encrypt/decrypt tests
├── Sorcha.TransactionHandler.Tests/
│   └── Encryption/
│       ├── DisclosureGroupBuilderTests.cs    # NEW: Grouping algorithm tests
│       └── EncryptionPipelineServiceTests.cs # NEW: Pipeline orchestration tests
├── Sorcha.Blueprint.Service.Tests/
│   └── Services/
│       ├── EncryptionBackgroundServiceTests.cs  # NEW: Async pipeline tests
│       └── ActionExecutionServiceEncryptionTests.cs  # NEW: Integration with encryption
├── Sorcha.Validator.Service.Tests/
│   └── Services/
│       └── TransactionReceiverSizeTests.cs   # NEW: Size enforcement tests
└── Sorcha.Wallet.Service.Tests/
    └── Endpoints/
        └── MlKemDecapsulateTests.cs          # NEW: Fixed decapsulate tests
```

**Structure Decision**: Existing microservices structure. No new projects — all changes fit within existing project boundaries. New files are added to existing directories following established folder conventions. The `Encryption/` subdirectory in TransactionHandler groups the new encryption pipeline code.

## Complexity Tracking

No constitution violations to justify. All changes use existing patterns:
- `IOptions<T>` for configuration
- `BackgroundService` for async processing
- `IHubContext<ActionsHub>` for SignalR
- `ISymmetricCrypto` / `ICryptoModule` for crypto operations
- Minimal API endpoints for new REST endpoints
- Standard xUnit + FluentAssertions for tests
