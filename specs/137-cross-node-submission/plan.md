# Implementation Plan: Cross-node submission round-trip (Stage 5)

**Branch**: `137-cross-node-submission` | **Date**: 2026-05-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/137-cross-node-submission/spec.md`
**Design**: `docs/superpowers/specs/2026-05-23-cross-node-submission-design.md` · **Research**: [research.md](./research.md)

## Summary

Enable the citizen→credential round-trip across two Sorcha installations: a citizen on a SyncOnly **replica** submits an AssuredIdentity application that the **owner** node validates, seals, approves (verification-analyst), and returns as an `AssuredIdentityCredential` to the citizen's local wallet. Trust stays two-plane — separate F136 installations bridged only at the ledger plane (genesis validator roster + tx/docket signatures). Five components: published-store-aware blueprint resolution (C1), event-driven blueprint recovery (C2), an open-participant derived-public-key field with published-record→carried-key→fail-closed resolution and the SD-JWT `cnf` binding (C3), F108 fan-out config (C4), and the mirror-instance submission fix that lets the analyst act on the owner node (C5 — confirmed by research to be a structural fix, not a check).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: ASP.NET Core Minimal APIs, .NET Aspire 13, YARP, Grpc.Net, StackExchange.Redis, JsonSchema.Net, Sorcha.Cryptography (libsodium/NSec), MudBlazor (Blazor WASM PWA + web)
**Storage**: PostgreSQL (EF Core; Blueprint instances, Wallet), Redis (events/pub-sub, mempool, caches), MongoDB (Register ledger)
**Testing**: xUnit + FluentAssertions + Moq; SQLite in-memory for EF service tests; Playwright for PWA E2E (Docker)
**Target Platform**: Linux containers (Docker / Aspire); Blazor WASM PWA
**Project Type**: Multi-service .NET solution (microservices) + Blazor front-ends
**Performance Goals**: Round-trip completes within normal interactive latency; new register → usable blueprint within 30s (SC-003); no added per-request hot-path cost beyond one relationship lookup at instance creation
**Constraints**: Two installations remain separate identity domains (no cross-installation JWT, FR-016); fail-closed credential issuance (FR-012); cross-node live test runs on a separate machine (genesis key + n1 SSH)
**Scale/Scope**: 2 nodes, 1 register, 1 blueprint (AssuredIdentity) for v1; design must not preclude more

## Constitution Check

*GATE: must pass before Phase 0 (passed) and re-checked after Phase 1 (passed).*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes are within existing services (Blueprint, Wallet, Register, Validator) + shared libs; no new upward deps. Blueprint→Register relationship lookup uses the existing HTTP client. |
| II. Security First | PASS | No secrets committed; reuses genesis key out-of-band. Carried keys are **public** only; private keys never leave the wallet. Fail-closed issuance (FR-012). Input validation via FluentValidation on the new endpoint + field. Two-plane trust preserved (FR-016). |
| III. API Documentation | PASS | New Wallet-Service public-key endpoint gets OpenAPI `.WithSummary()/.WithDescription()` + XML docs; contract in `/contracts/`. |
| IV. Testing | PASS | Unit + single-node integration on the build machine (SC-005); cross-node E2E scripted for the separate machine. Target >85% on new code. |
| V. Code Quality | PASS | Nullable enabled, async I/O, DI, no new warnings. |
| VI. Blueprint Standards | PASS | AssuredIdentity blueprint stays JSON; the new `sorcha-holder-key` field is a JSON-Schema extension (F085/F092/F103 idiom), not C#. |
| VII. Domain-Driven Design | PASS | Uses Register/Blueprint/Action/Participant/Publish terms; "owner/replica/validator" are established F108 relationship terms. |
| VIII. Observability | PASS | New OTel counters for recovery-on-event, key-resolution outcome (published/carried/fail-closed), and fan-out; structured logging, no interpolation. |

**No constitution violations.** Complexity Tracking table omitted (nothing to justify).

## Project Structure

### Documentation (this feature)

```text
specs/137-cross-node-submission/
├── plan.md              # This file
├── research.md          # Phase 0 — resolved unknowns
├── data-model.md        # Phase 1 — entities & field shapes
├── quickstart.md        # Phase 1 — single-node test plan + cross-node scripted procedure
├── contracts/           # Phase 1 — new endpoint + schema-extension contracts
└── tasks.md             # Phase 2 — /speckit.tasks (NOT created here)
```

### Source Code (touch-points by component)

```text
src/Services/Sorcha.Blueprint.Service/
├── Program.cs                                   # C1: CreateInstance published-store fallback + owner-gated publish
├── Services/Implementation/BlueprintRecoveryService.cs   # C2: subscribe register:created + per-register recovery
├── Services/Implementation/ActionExecutionService.cs     # C3: cnf holder JWK + recipient-key precedence; C5: mirror-aware submit
├── Services/Implementation/InstanceMirrorReconstructor.cs# C5: seed CurrentActionIds (blueprint-aware) 
├── Storage/{EfCoreInstanceStore,InMemoryInstanceStore}.cs# C5: mirror-aware update path
└── (config)                                     # C4: IPeerServiceClient BaseAddress

src/Services/Sorcha.Validator.Service/
└── Services/DocketBuildTriggerService.cs         # C5: emit NextActionId in authoritative tx metadata

src/Services/Sorcha.Wallet.Service/
├── Endpoints/CredentialEndpoints.cs              # C3: HolderJwk → SD-JWT cnf
├── Endpoints/CitizenWalletEndpoints.cs (or new)  # C3: new public-key endpoint (holder JWK + X25519)
└── Services/Implementation/HolderKeyService.cs    # C3: X25519 public-key accessor

src/Common/
├── Sorcha.Register.Core/Events/RegisterEventChannels.cs  # C2: RegisterCreated constant
├── Sorcha.Blueprint.Models/Control.cs            # C3: ControlTypes.HolderKey
├── Sorcha.Blueprint.Models/ (HolderKeySchemaExtension.cs)# C3: x-holder-key parser (optional)
├── Sorcha.ServiceClients.Http/Wallet/            # C3: IWalletServiceClient.IssueCredentialAsync HolderJwk; public-key client
└── Sorcha.Blueprint.Models/Credentials/          # C3: CredentialIssuanceConfig (no AcceptedIssuers churn)

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
├── Services/User/Forms/FormSchemaService.cs      # C3: format → ControlTypes.HolderKey
├── Components/Forms/ControlDispatcher.razor       # C3: dispatch HolderKey
└── Components/Forms/Controls/HolderKeyRenderer.razor  # C3: NEW — autofill from Wallet-Service endpoint

src/Apps/Sorcha.Wallet.Pwa/
├── Pages/ApplicationInstance.razor               # C3: wire real SorchaFormRenderer submit (replace stub)
└── Services/Applications/IApplicationSubmissionService.cs # C3: real submission (replace StubApplicationSubmissionService)

walkthroughs/AssuredIdentity/                      # SC-001: cross-node scripted verification procedure

tests/ (per service)                               # unit + single-node integration (SC-005)
```

**Structure Decision**: Brownfield — extend existing services and shared libraries; one new Blazor renderer component, one new Wallet-Service endpoint, one new event constant. No new project.

## Phase 1 outputs

- `data-model.md` — the `holderKeys` field value, the public-key endpoint DTOs, the `cnf`/issuance-request additions, the `NextActionId` metadata addition, and mirror-instance field changes.
- `contracts/` — OpenAPI for the new Wallet-Service public-key endpoint; the `sorcha-holder-key` / `x-holder-key` schema-extension contract.
- `quickstart.md` — build-machine test plan (unit + single-node integration) and the cross-node scripted verification procedure (deferred machine).

## Risks & sequencing

- **C5 first** — highest risk/effort and a hard gate on the round-trip; validate the mirror-aware submit + `NextActionId` seeding with a single-node integration test before cross-node work.
- **C1/C2** unblock instance creation on the replica — needed before any submission can be authored.
- **C3** spans server (cnf, endpoint, precedence) + client (field, PWA wiring); the AEAD side reuses `ExternalRecipientKeys`, the `cnf` side is net-new.
- **C4** is config; assert it via integration but it carries no design risk.
