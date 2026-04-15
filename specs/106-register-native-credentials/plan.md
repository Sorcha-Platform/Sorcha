# Implementation Plan: Register-native credential delivery

**Branch**: `106-register-native-credentials` | **Date**: 2026-04-15 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/106-register-native-credentials/spec.md`

## Summary

Feature 106 adds a second credential delivery mode to Sorcha blueprint actions — one that delivers the minted credential directly via a recipient-encrypted disclosure sealed into the issuing action's register transaction, rather than through the existing OpenID4VCI pre-authorized-code flow. The holder's Wallet Service detects the inbound credential on the existing bloom-filter notification path (`NotificationDeliveryService.DeliverAsync`), decrypts it with the local wallet's private key, and persists it as `PendingAcceptance`. The holder's Blueprint Service reconstructs a read-only mirror of the issuing instance from observing peer-replicated `docket:confirmed` events so the holder sees the pending accept/reject action in their normal MyActions surface. The existing wave 14b `CredentialClaimCard` (finally working in the browser after PR #290) drives both accept and decline, with accept sealing a blueprint action execution transaction and decline sealing a blueprint action rejection transaction. No direct node-to-node RPC — the register is the only cross-node channel.

**Design document (authoritative HOW):** `docs/superpowers/specs/2026-04-14-register-native-credential-delivery-design.md`. This plan binds against that design's wave breakdown plus the speckit spec's functional requirements.

## Technical Context

**Language/Version**: C# 13 on .NET 10.0 (existing Sorcha stack — no changes)
**Primary Dependencies**:
- `Sorcha.TransactionHandler.Encryption.EncryptionPipelineService` (recipient-addressed X25519 wrap + XChaCha20-Poly1305 AEAD — reused as-is)
- `Sorcha.Haip.Service.HaipCredentialMinter.MintCredentialAsync` (SD-JWT VC mint with `cnf` binding — reused as-is)
- `Sorcha.Wallet.Service.NotificationDeliveryService.DeliverAsync` (bloom-filter notification hook point — extended, not replaced)
- `Sorcha.Blueprint.Service.TransactionLifecycleEventBridge` (Redis `docket:confirmed` subscriber — reused as the mirror reconstruction trigger)
- `Sorcha.Blueprint.Models.CredentialIssuanceConfig.TargetAudience` (existing enum — new `SorchaLocalWallet` value added)
- `Sorcha.Wallet.Portable.Domain.Entities.CredentialEntity` + `CredentialStatus` enum (existing table — new `PendingAcceptance` and `Declined` status values added)
- `Sorcha.Blueprint.Models.Action.RejectionConfig` (existing `IsTerminal` rejection machinery — reused for Action 3 decline path)

**Storage**:
- PostgreSQL via EF Core for Wallet Service credential store (existing `CredentialEntity` table gains new enum values, no schema migration needed beyond the enum mapping update)
- PostgreSQL via EF Core for Blueprint Service instance store (existing `Instances` table gains an `IsReadOnlyMirror` boolean column via a new EF migration)
- Redis pub/sub for `docket:confirmed` and `wallet:notifications` channels (existing infrastructure — no new channels)
- MongoDB for register transaction storage (existing — no changes)

**Testing**:
- xUnit v3.2.2 + FluentAssertions 8.8.0 + Moq 4.20.72 (existing conventions)
- Unit tests for each new service (inbound credential detector, mirror reconstructor, engine branch)
- Integration tests via the existing Docker-backed test infrastructure (`tests/Sorcha.UI.E2E.Tests` pattern)
- Cross-node integration test runs against a two-node docker-compose shape using the `DistributedRegister` walkthrough pattern as a template
- Playwright E2E test extending the existing `HaipVerifiedCitizen` coverage to exercise the new pending-credential inbox path

**Target Platform**: Linux containers (Docker Compose + Kubernetes-ready via .NET Aspire) — no platform changes
**Project Type**: Multi-service .NET platform — affects Blueprint Service, Wallet Service, Sorcha.UI Core/Web Client, Sorcha.Blueprint.Models, Sorcha.Wallet.Portable. No new projects.
**Performance Goals**:
- Holder sees pending credential within 30 seconds of tx sealing on issuer node, 95% of runs (SC-002 from spec)
- Accept → instance completed round-trip within 30 seconds of holder click, 95% of runs (SC-003)
- Local accept state transition <100ms (the wallet PATCH call; the register round-trip is the async tail)
- Bloom-filter false-positive handling has zero measurable overhead on the notification pipeline (new detector short-circuits on first decrypt attempt failure)
**Constraints**:
- MUST NOT introduce node-to-node RPC (FR-019)
- MUST NOT introduce new cryptographic primitives (FR-004 + design doc section 2)
- MUST preserve `HaipExternalWallet` delivery mode without regression (FR-002, SC-007)
- MUST work on the single-node docker-compose shape identically to the federated case (User Story 2, SC-008)
**Scale/Scope**:
- In scope: ~15-20 source files across 5 projects
- 6 implementation waves (per design doc §11), each independently committable
- One new EF migration (Blueprint Service, for `IsReadOnlyMirror` column)
- Zero database schema migrations for Wallet Service (enum extension only)
- Estimated ~1500-2000 lines of production code + tests across all waves

## Constitution Check

Evaluating against `.specify/memory/constitution.md`.

| Principle | Status | Notes |
|---|---|---|
| **I. Microservices-First Architecture** | ✅ PASS | Feature touches Blueprint Service, Wallet Service, and the shared `Sorcha.Blueprint.Models` contract. No upward dependencies introduced. The mirror reconstructor and inbound credential detector are both new background services living inside their respective service boundaries. No cross-service shared state beyond the register itself. |
| **II. Security First** | ✅ PASS | Reuses existing `EncryptionPipelineService` (X25519 + XChaCha20-Poly1305 AEAD) — no new crypto. All new endpoints go through existing JWT auth middleware. The new `/api/v1/wallets/by-owner/{ownerId}` endpoint introduced in Fix A already enforces `RequireService` policy. Input validation on the new enum values handled by FluentValidation on the existing request DTOs. |
| **III. API Documentation** | ✅ PASS | New endpoints inherit the existing `.WithSummary` / `.WithDescription` / `.WithOpenApi` conventions. Scalar UI surfaces them automatically. XML docs required on all new public methods. |
| **IV. Testing Requirements** | ⚠️ CAVEAT | Target 85% coverage applies. Wave 1 (engine branch) + Wave 2 (wallet store enum extension) are unit-testable directly. Wave 3 (inbound detector) and Wave 4 (mirror reconstructor) are integration-test territory. Wave 6 (cross-node) requires a two-node docker-compose harness. **Risk:** the existing Blueprint.Service.Tests and Validator.Service.Tests projects have pre-existing compile failures (MEMORY.md), so new tests in those projects may be blocked. Mitigation: gate the plan on a pre-wave "unblock test projects" task if needed, and otherwise write tests in the healthy projects (`Sorcha.UI.Core.Tests`, `Sorcha.Wallet.Core.Tests`, etc.). Noted in the Complexity Tracking section below. |
| **V. Code Quality** | ✅ PASS | C# 13, async/await, DI, nullable reference types all already in force across the affected projects. No new patterns introduced. |
| **VI. Blueprint Creation Standards** | ✅ PASS | All template changes are to existing JSON blueprint files (`walkthroughs/HaipVerifiedCitizen/blueprints/verified-citizen.json`). No Fluent API usage. |
| **VII. Domain-Driven Design** | ✅ PASS | Ubiquitous language preserved — Blueprint/Action/Participant/Disclosure terminology stays intact. New concepts (PendingAcceptance, DeliveryMode, instance mirror) named consistently with the existing vocabulary. |
| **VIII. Observability by Default** | ✅ PASS | New components extend existing OpenTelemetry spans. `NotificationDeliveryService` already has structured logging + metrics; the new credential detection step joins that same metric surface. Mirror reconstruction emits an `instance-mirror-created` log event. |

**Gate result**: ✅ PASS with one caveat tracked in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/106-register-native-credentials/
├── plan.md                                          # This file (/speckit.plan output)
├── research.md                                      # Phase 0 — all clarifications resolved against the design doc
├── data-model.md                                    # Phase 1 — entity + state machine additions
├── quickstart.md                                    # Phase 1 — how to run Feature 106 end-to-end on n1 + on a 2-node shape
├── contracts/
│   ├── credential-issuance-config.md                # Blueprint JSON schema delta for SorchaLocalWallet target
│   ├── inbound-credential-detection.md              # NotificationDeliveryService extension contract
│   ├── instance-mirror-reconstructor.md             # Blueprint Service background service contract
│   ├── holder-accept-reject-api.md                  # Client-facing API shape for accept/decline
│   └── credential-status-enum.md                    # Wallet store status machine
├── checklists/
│   └── requirements.md                              # Speckit quality checklist (from /speckit.specify — already exists)
├── spec.md                                          # Business contract (from /speckit.specify — already exists)
└── tasks.md                                         # Phase 2 output (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

This feature touches five existing projects. No new projects or directories at the root level — everything is an addition to or modification of existing Sorcha services and shared libraries.

```text
src/
├── Common/
│   └── Sorcha.Blueprint.Models/
│       └── Credentials/
│           └── CredentialIssuanceConfig.cs                       # + SorchaLocalWallet enum value on TargetAudience
├── Services/
│   ├── Sorcha.Blueprint.Service/
│   │   ├── Services/Implementation/
│   │   │   ├── ActionExecutionService.cs                         # + SorchaLocalWallet branch in ExecuteAsync (Wave 1)
│   │   │   └── InstanceMirrorReconstructor.cs                    # NEW background service (Wave 3)
│   │   ├── Storage/
│   │   │   ├── EfCoreInstanceStore.cs                            # + IsReadOnlyMirror filter guard (Wave 3)
│   │   │   └── Migrations/
│   │   │       └── 20260415_AddReadOnlyMirrorColumn.cs           # NEW EF migration (Wave 3)
│   │   └── Program.cs                                            # + DI registration for reconstructor
│   └── Sorcha.Wallet.Service/
│       ├── Services/
│       │   ├── Interfaces/
│       │   │   └── IInboundCredentialDetector.cs                 # NEW interface (Wave 2)
│       │   └── Implementation/
│       │       ├── InboundCredentialDetector.cs                  # NEW default impl (Wave 2)
│       │       └── NotificationDeliveryService.cs                # + Step 2b hook (Wave 2)
│       ├── Endpoints/
│       │   └── CredentialEndpoints.cs                            # + status filter param + PATCH status
│       └── Program.cs                                            # + DI for detector
├── Core/
│   └── Sorcha.Wallet.Portable/
│       └── Domain/
│           └── Entities/
│               └── CredentialEntity.cs                           # + PendingAcceptance, Declined enum values (Wave 2)
└── Apps/
    └── Sorcha.UI/
        └── Sorcha.UI.Web.Client/
            └── Pages/
                └── MyCredentials.razor                           # + PENDING tab wired to status filter (Wave 5)

tests/
├── Sorcha.UI.Core.Tests/                                         # existing healthy project — primary test home
├── Sorcha.Wallet.Core.Tests/                                     # existing — detector unit tests
└── Sorcha.UI.E2E.Tests/
    ├── HaipVerifiedCitizenRegisterNativeTests.cs                 # NEW Playwright E2E (Wave 5)
    └── HaipExternalWalletRegressionTests.cs                      # NEW regression guard (Wave 5)

walkthroughs/
└── HaipVerifiedCitizen/
    ├── blueprints/
    │   └── verified-citizen.json                                 # Action 2 targetAudience → SorchaLocalWallet (Wave 6)
    └── run.ps1                                                   # NEW assert on inbox-first claim flow (Wave 6)

docs/
├── superpowers/specs/
│   └── 2026-04-14-register-native-credential-delivery-design.md  # Existing design doc — authoritative HOW
└── reference/
    └── API-DOCUMENTATION.md                                      # Updated endpoint reference (Wave 6)

.claude/skills/
├── blueprint-builder/SKILL.md                                    # Default credential example → register-native (Wave 6)
└── walkthrough-builder/SKILL.md                                  # Updated holder expectations (Wave 6)
```

**Structure Decision**: Feature 106 is an extension of four existing Sorcha services — Blueprint Service, Wallet Service, UI Web Client, and shared `Sorcha.Blueprint.Models`. No new root-level directories, no new projects, no new solution entries. All new code lives inside existing service boundaries following the existing Folder Structure Convention from CLAUDE.md §Critical Patterns. The new background services (`InstanceMirrorReconstructor`, `InboundCredentialDetector`) follow the same shape as existing background services in their respective projects (`TransactionLifecycleEventBridge`, `NotificationDigestWorker`) and are registered in `Program.cs` via the existing `AddHostedService<T>` pattern.

## Complexity Tracking

| Violation / Caveat | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| **Blueprint.Service.Tests + Validator.Service.Tests pre-existing compile failures block new tests in those projects** | The new engine branch (Wave 1) and the mirror reconstructor (Wave 3) naturally belong in `Sorcha.Blueprint.Service.Tests`. Both test projects are in a broken state per MEMORY.md and new tests can't run there without first repairing the existing fixtures — which is out of scope for Feature 106 and would balloon its size. | **Option A rejected**: "Just fix the test projects first as a pre-wave." This is tens of files of stale mock wiring with no owner and would delay Feature 106 by days. The broken tests are unrelated to this feature. **Option B rejected**: "Write tests inline in the source projects as `#if DEBUG` blocks." Non-idiomatic, clutters production code, and bypasses CI gating anyway since CI doesn't run `#if DEBUG` blocks. **Chosen mitigation**: write unit tests for the detector and mirror reconstructor in `Sorcha.Wallet.Core.Tests` and `Sorcha.UI.Core.Tests` where possible (using interface mocks), and cover the engine branch end-to-end via the Wave 5 Playwright integration test + Wave 6 live walkthrough. Create a tracked follow-up task for the project repair work that surfaces once Feature 106 ships. |
| **`CredentialStatus` enum extension without a full state machine library** | Adding `PendingAcceptance` and `Declined` via a plain enum extension is the simplest shape that matches every other status-field pattern in the Sorcha codebase today (`InstanceState`, `OrganizationStatus`, `TransactionType`, etc.). | **Option A rejected**: Formal state machine library (e.g. Stateless, Appccelerate). Non-idiomatic for this codebase — no other entity uses one — and would introduce a cross-cutting dependency for a single-entity benefit. **Option B rejected**: Separate `PendingCredentialEntity` and `DeclinedCredentialEntity` tables. Triples the storage layer surface area, splits queries across three tables, breaks the existing `MyCredentials` UI filter model, and the user explicitly answered "use a status enum" in the brainstorming step. |

## Phase 0: Outline & Research

**Output**: [`research.md`](./research.md)

All technical context questions are resolved — the upstream design document (`docs/superpowers/specs/2026-04-14-register-native-credential-delivery-design.md`) already went through the brainstorming and user-review cycle, and the debug trace against n1 verified the existing primitives (section 13 of the design doc cites each one with `file:line`). The research phase therefore consolidates decisions already made rather than exploring unknowns.

Research items generated and recorded in `research.md`:

1. **Decision: Use `EncryptionPipelineService` (X25519 + XChaCha20-Poly1305 AEAD) as the encryption primitive.** Not a new decision — confirmed during the Explore agent's primitives map. Rationale: already used by Feature 085 (file chunks), Feature 079 (trust hardening disclosures), and wave 14b (disclosure encryption). Alternatives considered: inventing a new envelope shape for credentials (rejected — violates "no new crypto" constraint); reusing the Wallet Service's direct-decrypt endpoint (rejected — that endpoint is session-scoped and doesn't support recipient-addressed payloads).

2. **Decision: Hook `NotificationDeliveryService.DeliverAsync` for inbound credential detection rather than adding a new background worker.** Rationale: the bloom-filter notification path already exists for wallet transaction awareness; inbound credential detection is conceptually "another thing that happens when a wallet-relevant tx lands". Reuse preserves a single observation point and leverages the existing SignalR notification emission. Alternatives considered: new `InboundCredentialWorker` background service (rejected — duplicates the register observation loop); polling the register on a timer (rejected — latency floor too high and fails the 30-second SC-002 target under load).

3. **Decision: Mirror instance state in the holder's Blueprint Service rather than querying the register directly at pending-actions query time.** Rationale: query-time register lookups don't scale (each `/api/actions/pending` call would fan out across every instance on the register); mirror reconstruction pays the cost once per confirmed transaction. Alternatives considered: query-time register lookup (rejected for scale); sharing instance state across nodes via gossip (rejected — violates "register is the only cross-node channel").

4. **Decision: Reuse the wave 14b `CredentialClaimCard` dialog for the accept/reject UI** rather than building a new inbox-specific component. Rationale: the card was verified end-to-end in the browser as part of PR #290's debug trace, it already handles both accept and reject paths, and it already takes the offer data as a parameter so it doesn't care where the offer originated. Alternatives considered: new `InboxCredentialCard` component (rejected — duplicates existing functionality with no user-visible difference).

5. **Decision: `Declined` status is a terminal state retained in the wallet store, not a hard-delete.** Rationale: aligns with user instruction on audit trail retention during the brainstorm. Alternatives considered: hard-delete (rejected by user); soft-delete with TTL (rejected — adds a TTL concept the spec explicitly rules out in FR-024).

**All `NEEDS CLARIFICATION` markers from spec resolved in research.md**: zero markers in the spec (checklist passed on first run), so the research phase has no clarifications to resolve. It documents the above five decisions as entries in the standard Decision/Rationale/Alternatives format.

## Phase 1: Design & Contracts

**Prerequisites**: Phase 0 research.md complete.

Phase 1 produces the following artefacts inside `specs/106-register-native-credentials/`:

### data-model.md

Captures the data additions only (not the full entity model — Sorcha's existing entities are stable):

1. **`CredentialIssuanceConfig.TargetAudience` enum** — new `SorchaLocalWallet = 2` value alongside existing `SorchaInternal = 0` and `HaipExternalWallet = 1`. Default remains `SorchaInternal` for zero-config unchanged behaviour on existing blueprints.

2. **`CredentialStatus` enum** — new `PendingAcceptance = 4` and `Declined = 5` values alongside existing `Active = 0`, `Expired = 1`, `Revoked = 2`, `Suspended = 3`. State machine transitions:
   - `null` → `PendingAcceptance` (new inbound credential from `NotificationDeliveryService` detector)
   - `PendingAcceptance` → `Active` (holder clicks Accept)
   - `PendingAcceptance` → `Declined` (holder clicks Decline)
   - `PendingAcceptance` → `Expired` (credential's embedded `notValidAfter` passes before holder acts)
   - `Active` → `Expired` (same)
   - `Active` → `Revoked` (existing — status list driven)
   - `Declined` → (hard delete via explicit `DELETE /credentials/{id}` — terminal retention state otherwise)

3. **`Instance` entity** — new `IsReadOnlyMirror` boolean column (default `false`). EF migration adds the column with a default. `IInstanceStore.UpdateAsync` gains a precondition check: if the loaded row has `IsReadOnlyMirror = true` and the caller is not `InstanceMirrorReconstructor`, throw `InvalidOperationException`. Reconstructor calls a new internal `UpdateMirrorAsync` method that bypasses the check.

4. **`CredentialEntity`** — no new columns. Existing `IssuanceTxId`, `IssuanceBlueprintId`, `WalletAddress`, `Status`, `RawToken` fields carry all the state Feature 106 needs.

### contracts/

Five contract documents, one per architectural seam:

1. **`contracts/credential-issuance-config.md`** — the blueprint JSON schema delta showing an example Action 2 with `targetAudience: "SorchaLocalWallet"`, plus publish-time validation rules (recipientParticipantId must resolve; participant must be late-bindable or pre-bound; etc.).

2. **`contracts/inbound-credential-detection.md`** — the `IInboundCredentialDetector` interface signature, the `InboundCredentialExtract` return shape, detection rules (primary: blueprint action metadata declares `SorchaLocalWallet`; fallback: decrypted payload carries `Type: "credential-offer-v1"`), error handling (null on any extraction failure — never throws).

3. **`contracts/instance-mirror-reconstructor.md`** — the `InstanceMirrorReconstructor` background service contract: Redis `docket:confirmed` subscription shape, the set of transaction fields required for a valid reconstruction (sender, blueprint id, instance id, participant wallets, next actions), idempotency guarantees (safe to replay any transaction), trust boundary (only validator-confirmed transactions trigger reconstruction).

4. **`contracts/holder-accept-reject-api.md`** — the client-facing HTTP contract for holder accept and reject:
   - `PATCH /api/v1/wallets/{walletAddress}/credentials/{credentialId}` with body `{ status: "Active" | "Declined" }` — returns `200` with updated entity.
   - `POST /api/instances/{instanceId}/actions/3/execute` with empty payload, holder wallet signature — seals accept transaction to register.
   - `POST /api/instances/{instanceId}/actions/3/reject` with optional `{ reason: string }` — seals rejection transaction via existing blueprint engine rejection path.
   - Parallel execution from the client: the PATCH and POST run concurrently; client waits for both before updating UI state.

5. **`contracts/credential-status-enum.md`** — the full state machine diagram (text-based), all transitions, which service is authoritative for each transition, and the SignalR event shape for notifying the UI of state changes.

### quickstart.md

A runnable "how to verify Feature 106 end-to-end" guide covering:

1. **Single-node quickstart** — fresh `docker-compose up`, sign up a public user, submit Verified Citizen, approve as assessor, accept credential from the MyCredentials inbox. Expected outcome matches User Story 2's acceptance scenarios.

2. **Two-node quickstart** — `docker-compose.federation.yml` (new — a two-node shape copying the `DistributedRegister` walkthrough pattern), run the same flow with issuer on node A and holder on node B. Expected outcome matches User Story 1's acceptance scenarios.

3. **External wallet regression check** — run `walkthroughs/HaipDrivingLicence/run.ps1` unchanged. Expected outcome: no regression (User Story 3).

4. **Decline path** — repeat the single-node flow but click DECLINE instead of CLAIM. Expected outcome: credential moves to declined, issuer instance closes as rejected (User Story 4).

### Agent context update

Runs `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude` after Phase 1 artefacts are written. This adds the new primitives to the Claude Code project context file so future agent runs know Feature 106's vocabulary without re-reading the design doc.

### Re-evaluate Constitution Check post-design

**Run date**: 2026-04-15, after Phase 1 artefacts produced.

Re-evaluating each principle against the completed data-model, contracts, and quickstart:

| Principle | Post-design status | Delta from pre-design check |
|---|---|---|
| **I. Microservices-First Architecture** | ✅ PASS | Unchanged. The contracts confirm the separation: Wallet Service owns the credential store + inbound detection, Blueprint Service owns the engine branch + mirror reconstructor, UI Core owns the client-side accept/decline orchestration. No cross-service shared state introduced. |
| **II. Security First** | ✅ PASS | Unchanged. The data-model.md and `inbound-credential-detection.md` confirm no new crypto primitives — `EncryptionPipelineService` is reused end-to-end. All new endpoints inherit existing JWT auth. The signature verification on accept/reject transactions uses the existing `ActionExecutionService` verification path. |
| **III. API Documentation** | ✅ PASS | Unchanged. `holder-accept-reject-api.md` specifies the new status filter parameter on `GET /credentials` and the enum values in request bodies; XML docs and OpenAPI examples are standard requirements for all new surfaces. |
| **IV. Testing Requirements** | ⚠️ CAVEAT (unchanged) | The contracts call out testing strategy for each layer. The pre-design caveat about broken `Blueprint.Service.Tests` / `Validator.Service.Tests` projects still applies; all new unit tests route through `Sorcha.UI.Core.Tests`, `Sorcha.Wallet.Core.Tests`, or new dedicated test projects. Integration coverage via Wave 5 Playwright and Wave 6 walkthrough gates. |
| **V. Code Quality** | ✅ PASS | Unchanged. Every new code file follows existing conventions — async/await, DI, nullable reference types enabled. No compiler warnings in Release builds is enforced by the existing CI. |
| **VI. Blueprint Creation Standards** | ✅ PASS | Confirmed by `credential-issuance-config.md` — all blueprint changes are JSON template edits. Fluent API not touched. |
| **VII. Domain-Driven Design** | ✅ PASS | Contracts use the ubiquitous language consistently: Blueprint, Action, Participant, Disclosure, Instance. The new concepts (DeliveryMode, MirrorReconstructor, InboundCredentialDetector) extend the vocabulary without renaming anything. |
| **VIII. Observability by Default** | ✅ PASS | Each contract defines its metrics and log events. `InboundCredentialDetector` and `InstanceMirrorReconstructor` emit OpenTelemetry metrics with labelled outcomes. Structured logging required throughout. |

**Gate result**: ✅ PASS with the same caveat as pre-design (broken test projects blocking tests in-place). No new violations surfaced in Phase 1. Plan is cleared for Phase 2 task generation via `/speckit.tasks`.

## Phase 2 (next command)

`speckit.tasks` generates the dependency-ordered task list binding the plan to concrete commits. Not run here — that's the next command in the speckit workflow.

## Notes on wave breakdown

The design document defines six implementation waves (§11 of the design doc). The plan above maps each wave to its affected files and services; `speckit.tasks` will turn each wave into concrete tasks with file paths, test gates, and dependency ordering. Waves remain:

1. **Engine branch** — `ActionExecutionService.ExecuteAsync` gains `SorchaLocalWallet` handling, credential sealed into Action 2 disclosures.
2. **Wallet store status extension** — enum values + repository filters + PATCH endpoint.
3. **Inbound credential detection** — `IInboundCredentialDetector` + hook into `NotificationDeliveryService.DeliverAsync`.
4. **Instance mirror reconstruction** — `InstanceMirrorReconstructor` background service + `IsReadOnlyMirror` column + EF migration + `IInstanceStore` write guard.
5. **UI + Playwright** — `MyCredentials` PENDING tab wiring + claim-card accept/decline handler + E2E tests.
6. **Walkthrough migration + docs** — Verified Citizen walkthrough default, blueprint-builder skill update, API documentation, cross-node quickstart.

Each wave is independently reviewable and mergeable. Waves 1 and 2 have no inter-dependencies and could be parallelised; wave 3 depends on wave 2; wave 4 depends on nothing except its own EF migration; wave 5 depends on 2-4; wave 6 depends on 5.
