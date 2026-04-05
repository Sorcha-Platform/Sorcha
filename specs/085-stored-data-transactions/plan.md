# Implementation Plan: Stored Data Transactions

**Branch**: `085-stored-data-transactions` | **Date**: 2026-04-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/085-stored-data-transactions/spec.md`
**Design**: [2026-04-05-stored-data-transactions-design.md](../../docs/superpowers/specs/2026-04-05-stored-data-transactions-design.md)

## Summary

Enable binary file attachments (photos, PDFs, evidence files) as first-class fields in blueprint action schemas. Files are transparently chunked into ≤4MB transactions, encrypted using HKDF-derived per-chunk keys, submitted through the existing validator → register pipeline, and sealed in the same docket as the parent action. The Wallet Service mediates file retrieval by fetching chunks, decrypting, reassembling, and streaming decrypted files to the UI. Phase 1 uses existing storage providers with a 40MB ceiling.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Sorcha.TransactionHandler, Sorcha.Cryptography, Sorcha.Blueprint.Models, Sorcha.Blueprint.Engine, Sorcha.Validator.Core, Sorcha.Wallet.Core, MudBlazor (UI)
**Storage**: MongoDB (register transactions), PostgreSQL (file metadata via EF Core), existing IActionStore
**Testing**: xUnit 3.2.2, FluentAssertions 8.8.0, Moq 4.20.72, Playwright (E2E)
**Target Platform**: Server (.NET 10 services) + Browser (Blazor WASM)
**Project Type**: Microservices (existing architecture — changes span multiple services)
**Performance Goals**: 40MB file upload within 60 seconds on broadband; file metadata display instant (in action payload)
**Constraints**: 4MB max chunk size, 10 max chunks per file, 40MB max total file size, 30-minute orphan timeout
**Scale/Scope**: Changes across 10 existing projects, no new projects

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes distributed across existing services (Blueprint, Validator, Wallet, Register). No new services. Dependencies flow downward. |
| II. Security First | PASS | HKDF key derivation, per-recipient key wrapping, SHA-256 integrity verification, JWT auth on download endpoint. No plaintext file content stored or transmitted without encryption. |
| III. API Documentation | PASS | New Wallet Service endpoint will have XML docs and OpenAPI via Scalar. |
| IV. Testing Requirements | PASS | Unit tests for chunking, HKDF, validation rules. Integration tests for submission flow. E2E tests for UI component. >85% target. |
| V. Code Quality | PASS | Async/await for all I/O. DI throughout. Nullable enabled. |
| VI. Blueprint Creation Standards | PASS | File fields declared in JSON schema with `x-file` extension. No code-based blueprint changes. |
| VII. Domain-Driven Design | PASS | Uses existing domain language: Action, Transaction, Docket, Participant. New terms (File Reference, File Chunk) are natural extensions. |
| VIII. Observability by Default | PASS | Chunk upload/download operations will emit structured logs and OpenTelemetry traces. |

No violations. No complexity justification needed.

### Post-Design Re-Check (Phase 1 complete)

All gates still pass after design phase:
- No new services introduced (Microservices-First: PASS)
- HKDF uses built-in `System.Security.Cryptography.HKDF` — no new crypto dependencies (Security First: PASS)
- OpenAPI contract defined in `contracts/wallet-file-download.yaml` (API Documentation: PASS)
- Test files mapped for all new classes (Testing Requirements: PASS)
- File fields declared in JSON Schema, no C# blueprint changes (Blueprint Standards: PASS)
- No new external dependencies added to any project (Code Quality: PASS)

## Project Structure

### Documentation (this feature)

```text
specs/085-stored-data-transactions/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── wallet-file-download.yaml
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Common/
│   ├── Sorcha.TransactionHandler/
│   │   ├── Encryption/HkdfKeyDerivation.cs          # NEW: HKDF chunk key derivation
│   │   ├── Chunking/FileChunker.cs                  # NEW: File splitting logic
│   │   ├── Chunking/FileChunkMetadata.cs             # NEW: Chunk metadata model
│   │   └── Payload/PayloadManager.cs                 # MODIFY: Support file chunk payloads
│   ├── Sorcha.Blueprint.Models/
│   │   ├── FileReference.cs                          # NEW: Runtime file reference model
│   │   ├── FileSchemaExtension.cs                    # NEW: x-file schema parsing
│   │   └── Control.cs                                # EXISTS: ControlTypes.File already defined
│   ├── Sorcha.Wallet.Core/
│   │   └── FileReassembly/FileReassemblyService.cs   # NEW: Chunk fetch + decrypt + reassemble
│   └── Sorcha.ServiceClients/
│       └── IWalletServiceClient.cs                   # MODIFY: Add file download method
├── Core/
│   ├── Sorcha.Blueprint.Engine/
│   │   └── Validation/FileReferenceValidator.cs      # NEW: Schema validation for file-reference fields
│   └── Sorcha.Validator.Core/
│       └── Rules/FileChunkValidationRule.cs           # NEW: Chunk existence, ordering, size, type rules
├── Services/
│   ├── Sorcha.Blueprint.Service/
│   │   ├── Services/Implementation/TransactionBuilderService.cs  # MODIFY: Chunk-aware file transactions
│   │   └── Services/Implementation/OrphanChunkCleanupService.cs  # NEW: Background cleanup
│   └── Sorcha.Wallet.Service/
│       └── Endpoints/FileDownloadEndpoints.cs        # NEW: GET /api/wallets/{address}/files/download
└── Apps/
    └── Sorcha.UI/
        └── Sorcha.UI.Web.Client/
            └── Components/Forms/FileReferenceField.razor  # NEW: Upload + display component

tests/
├── Sorcha.TransactionHandler.Tests/
│   ├── HkdfKeyDerivationTests.cs                    # NEW
│   └── FileChunkerTests.cs                          # NEW
├── Sorcha.Blueprint.Engine.Tests/
│   └── FileReferenceValidatorTests.cs               # NEW
├── Sorcha.Validator.Core.Tests/
│   └── FileChunkValidationRuleTests.cs              # NEW
├── Sorcha.Wallet.Core.Tests/
│   └── FileReassemblyServiceTests.cs                # NEW
├── Sorcha.Blueprint.Service.Tests/
│   └── TransactionBuilderServiceFileTests.cs        # NEW
├── Sorcha.Wallet.Service.IntegrationTests/
│   └── FileDownloadEndpointTests.cs                 # NEW
└── Sorcha.UI.E2E.Tests/
    ├── PageObjects/FileReferenceFieldPage.cs         # NEW
    └── Docker/FileAttachmentTests.cs                 # NEW
```

**Structure Decision**: All changes fit within existing project boundaries. No new projects needed. The feature is distributed across the existing microservices architecture as cross-cutting changes.

## Complexity Tracking

No constitution violations. No complexity justification needed.
