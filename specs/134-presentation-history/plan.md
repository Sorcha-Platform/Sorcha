# Implementation Plan: Cross-Device Citizen Presentation History

**Branch**: `134-presentation-history` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/134-presentation-history/spec.md`
**Source design**: `docs/superpowers/specs/2026-05-20-f114-us5-offline-presentation-reconciliation-design.md`

## Summary

Give the citizen's reported presentations a durable, citizen-owned home so their presentation history follows them across devices. PR2 already ships the report transport (`POST /api/v1/wallet/presentations/log`, the reporter, Redis SET-NX dedupe, and the `IPresentationLogForwarder` seam currently backed by a logging no-op). This feature replaces that no-op with a durable per-citizen store in the Wallet Service, adds read + delete endpoints, and wires the PWA Activity page to merge server history with the local log. No Blueprint Service change, no `IPresentationConsumer`, no register/ledger write — a free-standing offline presentation has no originating register (FR-010 / SC-004).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: ASP.NET Core Minimal APIs; EF Core 9 (Npgsql) on `WalletDbContext`; StackExchange.Redis (existing PR2 SET-NX dedupe — unchanged); FluentValidation; Blazor WASM (PWA); `Sorcha.CitizenWallet.Abstractions` (wire `PresentationLogEntry`); `Sorcha.ServiceDefaults.Storage` (`IStorageRegistrationLog`)
**Storage**: PostgreSQL via `WalletDbContext` for the durable per-citizen store (new `CitizenPresentationRecord` entity + migration); IndexedDB on the PWA for the existing local log (unchanged)
**Testing**: xUnit + FluentAssertions + Moq; `TestCitizenWalletDbContext` InMemory pattern for store tests; reflection-based static-handler invocation for endpoint tests (the established `CitizenWalletEnrolEndpointTests` pattern)
**Target Platform**: Linux container (Wallet Service); Blazor WASM PWA served at `/wallet/`
**Project Type**: web (backend service + WASM frontend)
**Performance Goals**: history read is not a hot path; typical list/delete well under 200 ms p95
**Constraints**: disclosed claim **names** only, never values (FR-002); **no register/ledger write** (FR-010); cross-user access 404-indistinguishable (FR-006); store registered via `IStorageRegistrationLog` (`RegisterPersistent` with Postgres, in-memory fallback) but **not** on the F113 fail-fast audited list — convenience data, warns but does not gate startup
**Scale/Scope**: per-citizen history, modest volume (tens–hundreds of entries per citizen); two new endpoints; one new EF entity + migration; PWA Activity merge

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Microservices-First | PASS | Wallet Service owns the store end-to-end. No upward dependency; Blueprint Service is untouched (the whole point of the reconciliation). Domain entity in `Sorcha.Wallet.Portable`. |
| II. Security First | PASS | Citizen JWT auth on both endpoints; cross-user reads/deletes 404-indistinguishable; input validated via FluentValidation (reuse the shipped `PresentationLogReportRequestValidator`); **no claim values persisted** (names only). No secrets. |
| III. API Documentation | PASS | Minimal API endpoints carry `.WithSummary()`/`.WithDescription()` + typed `.Produces<>()`; built-in OpenAPI; XML docs on all new public members. |
| IV. Testing | PASS | Store, forwarder, endpoint, client, and PWA-merge tests; target >85% for new code; deterministic (InMemory provider + mocks). |
| V. Code Quality | PASS | Nullable enabled, async I/O, DI, no Release warnings. |
| VI. Blueprint Standards | N/A | No blueprint or workflow changes. |
| VII. Domain-Driven Design | PASS | Uses domain vocabulary (Presentation, Credential, Verifier, Disclosure); the record holds disclosed *claim names* (Disclosure), not values. |
| VIII. Observability | PASS | OpenTelemetry counter for store operations (`upsert`/`list`/`delete`) on the existing `Sorcha.Wallet.Service` meter; structured logging (no interpolation). |

**Result**: All gates pass. No violations → Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/134-presentation-history/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (OpenAPI for the two new endpoints)
└── checklists/
    └── requirements.md  # /speckit.specify quality checklist (passed)
```

### Source Code (repository root)

```text
src/
├── Core/Sorcha.Wallet.Portable/Domain/Entities/
│   └── CitizenPresentationRecord.cs                 # NEW entity (WalletDbContext)
├── Services/Sorcha.Wallet.Service/
│   ├── Services/Interfaces/
│   │   └── ICitizenPresentationStore.cs             # NEW
│   ├── Services/Implementation/
│   │   ├── EfCoreCitizenPresentationStore.cs        # NEW (Postgres)
│   │   ├── InMemoryCitizenPresentationStore.cs      # NEW (fallback + tests)
│   │   └── CitizenPresentationStoreForwarder.cs     # NEW IPresentationLogForwarder — replaces LoggingPresentationLogForwarder
│   ├── Endpoints/CitizenWalletEndpoints.cs          # EDIT: GET + DELETE /api/v1/wallet/presentations
│   ├── Extensions/  (or Program.cs)                 # EDIT: register store + swap forwarder + IStorageRegistrationLog
│   └── Migrations/                                  # NEW EF migration for CitizenPresentationRecord
├── Common/Sorcha.ServiceClients.Http/CitizenWallet/
│   ├── ICitizenWalletClient.cs                      # EDIT: ListPresentationsAsync + DeletePresentationAsync
│   └── CitizenWalletClient.cs                       # EDIT: implementations
└── Apps/Sorcha.Wallet.Pwa/
    ├── Pages/Activity.razor                         # EDIT: merge server history (rule in §5 of design)
    └── Services/ (Activity feed/merge helper)       # EDIT: fetch + merge + delete-both

tests/
├── Sorcha.Wallet.Service.Tests/CitizenWallet/
│   ├── CitizenPresentationStoreTests.cs             # NEW
│   ├── CitizenPresentationStoreForwarderTests.cs    # NEW
│   └── PresentationHistoryEndpointTests.cs          # NEW (GET + DELETE handlers)
└── Sorcha.Wallet.Pwa.Tests/Services/
    └── ActivityMergeTests.cs                        # NEW (merge rule + delete-both)
```

**Structure Decision**: Web shape (Wallet Service backend + Blazor WASM PWA frontend), matching the existing Feature 114 layout. All new server code lives in the Wallet Service and the shared `Sorcha.Wallet.Portable` domain project; the client method lives in the shared `Sorcha.ServiceClients.Http` package the PWA already consumes; PWA changes are confined to the Activity surface.

## Complexity Tracking

> No Constitution Check violations — section intentionally empty.
