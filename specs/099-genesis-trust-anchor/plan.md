# Implementation Plan: System Register Genesis Trust Anchor

**Branch**: `099-genesis-trust-anchor` | **Date**: 2026-04-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/099-genesis-trust-anchor/spec.md`

## Summary

Replace the self-bootstrapping system register creation with an offline genesis ceremony that produces a pre-signed genesis block. Instances consume this artifact rather than creating their own, establishing a shared trust anchor for multi-instance networks. The genesis file is embedded in builds and verified during peer sync.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Sorcha.Cryptography (ED25519), System.CommandLine 2.0.2, Sorcha.ServiceClients.Http
**Storage**: Embedded assembly resource (genesis file), PostgreSQL (Wallet Service for key import)
**Testing**: xUnit + FluentAssertions + Moq (existing test infrastructure)
**Target Platform**: Linux containers (Docker), Windows development
**Project Type**: Distributed microservices (existing Sorcha platform)
**Performance Goals**: Genesis ceremony < 1 second, bootstrap flow < 30 seconds (existing timeout)
**Constraints**: Offline ceremony (no running services), genesis private key never in runtime config
**Scale/Scope**: 3 new CLI commands, 1 new endpoint, 1 service rewrite, 1 new verification component

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new services. Changes within existing service boundaries. CLI is standalone. |
| II. Security First | PASS | Core purpose is security improvement. Private key never touches runtime. Genesis signature verified cryptographically. |
| III. API Documentation | PASS | New wallet import endpoint will have XML docs + Scalar. CLI commands self-documenting via System.CommandLine. |
| IV. Testing Requirements | PASS | Unit tests for ceremony, verification, bootstrap flow. Integration test for full lifecycle. |
| V. Code Quality | PASS | Async/await, DI, nullable reference types. Following existing patterns. |
| VI. Blueprint Creation | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Uses ubiquitous language: Register, Docket, Validator, Blueprint. |
| VIII. Observability | PASS | Structured logging for all bootstrap states. Network ID logged on startup. |

**Gate result: PASS** — no violations.

## Project Structure

### Documentation (this feature)

```text
specs/099-genesis-trust-anchor/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research decisions
├── data-model.md        # Entity definitions
├── quickstart.md        # Operator guide
├── contracts/
│   ├── cli-commands.md          # CLI command contracts
│   └── wallet-import-endpoint.md # Wallet Service endpoint
└── checklists/
    └── requirements.md  # Quality checklist
```

### Source Code (repository root)

```text
src/
├── Apps/
│   └── Sorcha.Cli/
│       └── Commands/
│           └── SystemRegisterCommands.cs    # NEW: create, verify, import-validator-key
├── Common/
│   ├── Sorcha.Cryptography/                 # UNCHANGED: used directly for ceremony
│   ├── Sorcha.Register.Models/
│   │   ├── Constants/
│   │   │   └── SystemRegisterConstants.cs   # MODIFY: add GenesisPublicKeyFingerprint
│   │   ├── Genesis/
│   │   │   ├── SystemRegisterGenesis.cs     # NEW: genesis file model
│   │   │   ├── GenesisTransactionData.cs    # NEW: signed transaction model
│   │   │   ├── GenesisSignature.cs          # NEW: signature model
│   │   │   ├── GenesisValidatorKeyFile.cs   # NEW: key file model
│   │   │   └── GenesisFileLoader.cs         # NEW: config path + embedded resource loader
│   │   ├── Resources/
│   │   │   └── system-register-genesis.json # NEW: embedded resource (ceremony output)
│   │   └── Sorcha.Register.Models.csproj    # MODIFY: add EmbeddedResource
│   └── Sorcha.ServiceDefaults/
│       └── SystemRegisterOptions.cs         # NEW: config section model
├── Services/
│   ├── Sorcha.Register.Service/
│   │   └── Services/
│   │       └── SystemRegisterBootstrapper.cs # REWRITE: 4-step flow
│   ├── Sorcha.Peer.Service/
│   │   └── Replication/
│   │       └── SystemRegisterSyncVerifier.cs # NEW: genesis signature check
│   └── Sorcha.Wallet.Service/
│       └── Endpoints/
│           └── WalletEndpoints.cs           # MODIFY: add import-validator-key endpoint

tests/
├── Sorcha.Cli.Tests/
│   └── SystemRegisterCommandTests.cs        # NEW: ceremony + verify tests
├── Sorcha.Register.Models.Tests/
│   └── GenesisFileLoaderTests.cs            # NEW: loading + fallback tests
├── Sorcha.Register.Service.Tests/
│   └── SystemRegisterBootstrapperTests.cs   # REWRITE: 4 flow paths
└── Sorcha.Peer.Service.Tests/
    └── SystemRegisterSyncVerifierTests.cs   # NEW: verification tests
```

**Structure Decision**: All changes fit within existing project boundaries. No new projects needed. The genesis models live in `Sorcha.Register.Models` (owns system register constants and control record). The genesis file loader lives there too since it handles embedded resource resolution. The sync verifier lives in `Sorcha.Peer.Service` (owns replication pipeline).

## Complexity Tracking

No constitution violations to justify. All changes within existing project boundaries with no new abstractions beyond what the feature requires.
