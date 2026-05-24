---
description: "Task list for Federation Trust Hardening (Feature 138)"
---

# Tasks: Federation Trust Hardening

**Input**: Design documents from `/specs/138-federation-trust-hardening/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (all present)

**Tests**: REQUIRED. The spec's unifying criterion (FR-021 / SC-008) mandates an automated negative test proving the forged/unsigned/replayed variant is rejected for every hardened surface. Constitution Principle IV requires >85% coverage on new code. Tests are written test-first within each story.

**Organization**: Tasks are grouped by user story. Each story is an independently shippable slice (US1 → US6 in priority order).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: US1–US6 maps to spec user stories
- All paths are absolute-from-repo-root; types named per data-model.md / research.md

## Path Conventions

- Source: `src/Common/…`, `src/Services/…`, `src/Apps/…` (microservices layout from plan.md)
- Tests: `tests/<Project>.Tests/…` (xUnit + FluentAssertions + Moq)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration and observability scaffolding consumed across stories

- [ ] T001 [P] Add feature config keys with secure defaults per `contracts/config-and-metrics.md` to the relevant `appsettings.json` and bind via options classes: `Verifier:ClockSkewSeconds` (60), `Verifier:KbJwtMaxLifetimeSeconds` (120) in `src/Apps/Sorcha.Verifier/appsettings.json`; `PeerService:EnableTls`, `PeerService:ChallengeTtlSeconds` (30) in `src/Services/Sorcha.Peer.Service/appsettings.json`; `Consensus:LivenessTimeoutSeconds` (from policy `DocketTimeoutSeconds`) referenced in `src/Services/Sorcha.Validator.Service/appsettings.json`
- [ ] T002 [P] Declare the 8 new OTel rejection counters on the existing `Sorcha.Verifier` / `Sorcha.Peer` / `Sorcha.Validator` / `Sorcha.Blueprint` meters per `contracts/config-and-metrics.md` (instrument definitions only; incremented within each story)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared primitives that US1/US2/US5 (clock skew) and US1/US2 (health degradation) all depend on

**⚠️ CRITICAL**: Complete before starting the dependent user stories

- [ ] T003 Add a shared clock-skew helper bound to `Verifier:ClockSkewSeconds` in `src/Common/Sorcha.ServiceDefaults/` (consumed by US1 status-list freshness, US2 heartbeat timestamp, US5 KB-JWT exp) using the injected `TimeProvider`
- [ ] T004 [P] Add a reusable "security-posture degraded" health-check signal helper in `src/Common/Sorcha.ServiceDefaults/` following the Storage Registration Log pattern (CLAUDE.md §10/§11), used by US1 (status-list unverifiable) and US2 (mTLS unavailable)

**Checkpoint**: Shared config, metrics, clock-skew, and health signals ready — user stories can begin (in parallel if staffed).

---

## Phase 3: User Story 1 - Revocation cannot be forged (Priority: P1) 🎯 MVP

**Goal**: The verifier authenticates the revocation status list against a sealed-state-anchored key and fails closed when it cannot.

**Independent Test**: Serve a forged/unsigned/wrong-issuer status list and confirm the credential is rejected; block the fetch and confirm fail-closed; confirm a genuine list still correctly reports revoked/valid. (quickstart US1)

### Tests for User Story 1 ⚠️ (write first, must fail)

- [ ] T005 [P] [US1] Negative test: forged-signature status list rejected, in `tests/Sorcha.Verifier.Engine.Tests/StatusListSignatureTests.cs`
- [ ] T006 [P] [US1] Negative test: `iss` mismatch rejected even when signature internally valid, in `tests/Sorcha.Verifier.Engine.Tests/StatusListIssuerPinningTests.cs`
- [ ] T007 [P] [US1] Negative test: fetch failure fails closed (no stale-cache serve), in `tests/Sorcha.Verifier.Engine.Tests/StatusListFailClosedTests.cs`
- [ ] T008 [P] [US1] Negative test: expired list rejected (no +24h default), and honest-path: genuine signed list reports revoked correctly, in `tests/Sorcha.Verifier.Engine.Tests/StatusListFreshnessTests.cs`

### Implementation for User Story 1

- [ ] T009 [US1] Inject `IIssuerKeyResolver` into `StatusListCache` and verify the JWT signature inside `ParseJwt()` before trusting any bit, in `src/Common/Sorcha.Verifier.Engine/StatusListCache.cs`
- [ ] T010 [US1] Pin `iss` to the expected org DID and reject on mismatch in `src/Common/Sorcha.Verifier.Engine/StatusListCache.cs`
- [ ] T011 [US1] Make `GetOrFetchAsync` fail closed: remove stale-cache fallback on fetch/verify failure; only cache `Verified` lists, in `src/Common/Sorcha.Verifier.Engine/StatusListCache.cs`
- [ ] T012 [US1] Enforce freshness against list `exp` within clock skew (T003); remove the +24h default, in `src/Common/Sorcha.Verifier.Engine/StatusListCache.cs`
- [ ] T013 [US1] Ensure the consuming path treats "unverifiable" as fail (not "unknown ⇒ allowed") at the `IsRevokedAsync` call site, in `src/Common/Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs`
- [ ] T014 [P] [US1] Add a `kid` header identifying the signing verification method, in `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenStatusListPublisher.cs`
- [ ] T015 [US1] Wire the `IIssuerKeyResolver` into `StatusListCache` registration (DID-backed → JWK-registry composite; JWK-registry path dev-only), in `src/Apps/Sorcha.Verifier/Extensions/ServiceCollectionExtensions.cs`
- [ ] T016 [US1] Increment `sorcha_statuslist_rejected_total{reason}` on each rejection path, in `src/Common/Sorcha.Verifier.Engine/StatusListCache.cs`

**Checkpoint**: US1 fully functional and independently testable — revocation forgery is closed.

---

## Phase 4: User Story 2 - Peer identity & claims are provable (Priority: P1)

**Goal**: Nodes prove control of a cryptographic identity; advertisements/heartbeats are signed and replay-checked; transport is fail-closed outside dev; registration is rate-limited.

**Independent Test**: Attempt identity forgery, register-ownership spoofing, and heartbeat replay — all rejected; cleartext peer connection refused in prod profile; legitimate node restart re-registers fine. (quickstart US2)

### Tests for User Story 2 ⚠️ (write first, must fail)

- [ ] T017 [P] [US2] Negative test: `RegisterPeer` without valid challenge signature refused, in `tests/Sorcha.Peer.Service.Tests/PeerRegistrationSignatureTests.cs`
- [ ] T018 [P] [US2] Negative test: `peer_id` ≠ public-key thumbprint refused, in `tests/Sorcha.Peer.Service.Tests/PeerIdentityBindingTests.cs`
- [ ] T019 [P] [US2] Negative test: replayed heartbeat (stale sequence/timestamp) rejected, in `tests/Sorcha.Peer.Service.Tests/HeartbeatReplayTests.cs`
- [ ] T020 [P] [US2] Negative test: unsigned/wrong-key advertisement dropped and not propagated, in `tests/Sorcha.Peer.Service.Tests/AdvertisementSignatureTests.cs`
- [ ] T021 [P] [US2] Negative test: cleartext transport refused outside Development; allowed in Development, in `tests/Sorcha.Peer.Service.Tests/TransportFailClosedTests.cs`
- [ ] T022 [P] [US2] Negative test: registration rate limit returns `RESOURCE_EXHAUSTED`, in `tests/Sorcha.Peer.Service.Tests/PeerRateLimitTests.cs`

### Implementation for User Story 2

- [ ] T023 [P] [US2] Add proto changes (`RequestChallenge` RPC + `RegisterPeerRequest` public_key/timestamp/challenge_nonce/signature) in `src/Services/Sorcha.Peer.Service/Protos/peer_communication.proto`
- [ ] T024 [P] [US2] Add `signature` to `RegisterAdvertisement` and a signature to the heartbeat body in `src/Services/Sorcha.Peer.Service/Protos/peer_heartbeat.proto`
- [ ] T025 [P] [US2] Add `PublicKey`, `LastHeartbeatSequenceNumber`, `LastHeartbeatTimestamp` to `src/Services/Sorcha.Peer.Service/Models/PeerNode.cs`; bind `PeerId` to the public-key thumbprint
- [ ] T026 [US2] EF migration for the new `PeerNode` fields and the `NodeIdentity` table, in `src/Services/Sorcha.Peer.Service/` (set `$env:ConnectionStrings__Sorcha__Postgres`; do not use `--no-build`)
- [ ] T027 [US2] Create `NodeIdentityService` (ED25519 keygen via `CryptoModule`, private key encrypted via Key Protection Provider, public-key thumbprint as NodeId) in `src/Services/Sorcha.Peer.Service/Services/NodeIdentityService.cs`; bootstrap in `Program.cs`
- [ ] T028 [US2] Implement `RequestChallenge` + signature verification on `RegisterPeer` (reject bad signature / id mismatch / stale timestamp / unknown-expired challenge), in `src/Services/Sorcha.Peer.Service/GrpcServices/PeerDiscoveryServiceImpl.cs`
- [ ] T029 [US2] Validate `sequence_number` monotonicity + timestamp freshness (T003) and heartbeat body signature, in `src/Services/Sorcha.Peer.Service/GrpcServices/PeerHeartbeatGrpcService.cs`
- [ ] T030 [US2] Sign outgoing advertisements and verify incoming ones against the originating node's stored `PublicKey`; drop unsigned/bad, in `src/Services/Sorcha.Peer.Service/Services/RegisterAdvertisementService.cs` and `src/Services/Sorcha.Peer.Service/Services/PeerHeartbeatService.cs`
- [ ] T031 [US2] Make `PeerAuthInterceptor` fail closed outside Development (no silent anonymous; reject expired/missing auth), in `src/Services/Sorcha.Peer.Service/Interceptors/PeerAuthInterceptor.cs`
- [ ] T032 [US2] Enforce mTLS outside Development: gate the cleartext HTTP/2 switch on `PeerService:EnableTls`; configure Kestrel client-cert validation and client cert in `src/Services/Sorcha.Peer.Service/Program.cs` and `src/Services/Sorcha.Peer.Service/Services/PeerConnectionPool.cs`
- [ ] T033 [P] [US2] Create `RateLimitInterceptor` (gRPC `RESOURCE_EXHAUSTED`, fed by `RateLimitSettings`) and register it, in `src/Services/Sorcha.Peer.Service/Interceptors/RateLimitInterceptor.cs` + `Program.cs`
- [ ] T034 [US2] Increment `sorcha_peer_registration_rejected_total{reason}` and `sorcha_peer_message_rejected_total{reason}`; surface degraded health when mTLS unavailable (T004), across the peer GrpcServices

**Checkpoint**: US1 and US2 both independently functional.

---

## Phase 5: User Story 3 - Sealed-roster vote authority (Priority: P1)

**Goal**: Vote authority derives solely from the sealed on-chain roster; admission defaults to Consent; equivocation and withholding trigger automatic, deterministic, sealed ejection. Establishes the `PERM-*` primitives.

**Independent Test**: Out-of-roster vote gets zero quorum weight deterministically; new register defaults to Consent; equivocation auto-ejects with no operator action; withholding auto-ejects after timeout. (quickstart US3)

### Tests for User Story 3 ⚠️ (write first, must fail)

- [ ] T035 [P] [US3] Negative test: vote signed by key absent from sealed roster contributes zero quorum weight, deterministically across nodes, in `tests/Sorcha.Validator.Service.Tests/SealedRosterVoteAuthorityTests.cs`
- [ ] T036 [P] [US3] Test: `RegisterPolicy.CreateDefault()` defaults to Consent; self-registered validator is `Pending` and casts no counting vote, in `tests/Sorcha.Register.Models.Tests/RegistrationModeDefaultTests.cs`
- [ ] T037 [P] [US3] Test: equivocation produces a deterministic sealed `control.validator.eject` with zero operator actions; entry → `Ejected`, in `tests/Sorcha.Validator.Service.Tests/EquivocationEjectionTests.cs`
- [ ] T038 [P] [US3] Test: withholding produces a sealed `control.validator.liveness-violation` after the timeout, in `tests/Sorcha.Validator.Service.Tests/LivenessTimeoutEjectionTests.cs`
- [ ] T039 [P] [US3] Test: ejection that would break quorum surfaces the condition rather than silently bricking, in `tests/Sorcha.Validator.Service.Tests/QuorumGuardTests.cs`

### Implementation for User Story 3

- [ ] T040 [P] [US3] Flip `CreateDefault()` `RegistrationMode` `Public → Consent`, in `src/Common/Sorcha.Register.Models/RegisterPolicy.cs`
- [ ] T041 [P] [US3] Add `Ejected` status + `EjectionRef` to roster entries, in `src/Common/Sorcha.Register.Models/ValidatorRoster.cs`
- [ ] T042 [US3] Implement roster reconstruction from `RegisterControlRecord.Validators` (sealed) and make the `ValidatorRegistry` cache a derived, non-authoritative view (seal wins on divergence), in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [ ] T043 [US3] Derive vote authority from the sealed roster in `ValidateVotesAsync` (key ∈ sealed ActiveValidators ∧ signatureValid ∧ ¬doubleVote), in `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs`
- [ ] T044 [P] [US3] Define the `control.validator.eject` and `control.validator.liveness-violation` action types per `contracts/validator-control-transactions.md`, in `src/Common/Sorcha.Register.Models/`
- [ ] T045 [US3] Process the two new control actions (apply ejection to sealed roster state) in `src/Services/Sorcha.Validator.Service/Services/ControlDocketProcessor.cs`
- [ ] T046 [US3] Emit a deterministic `control.validator.eject` (carrying the two conflicting signed votes) on detected double-vote, replacing the in-memory-only log + manual revoke, in `src/Services/Sorcha.Validator.Service/Services/BadActorDetector.cs`
- [ ] T047 [US3] Detect accept-without-seal past `LivenessTimeoutSeconds` and emit `control.validator.liveness-violation`, in `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs`
- [ ] T048 [US3] Add the quorum guard (surface, don't silently drop below workable quorum; ties to GOV-5), in `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs`
- [ ] T049 [US3] Increment `sorcha_validator_vote_rejected_total{reason}` and `sorcha_validator_ejected_total{reason}`, across the validator services

**Checkpoint**: US1–US3 independently functional; on-chain-roster + deterministic-ejection primitives in place for `PERM-*`.

---

## Phase 6: User Story 4 - Verified blueprint recovery (Priority: P2)

**Goal**: Recovered blueprints are verified against a sealed content digest before storage.

**Independent Test**: Tampered blueprint (hash mismatch) and no-provenance blueprint both rejected; correctly-sealed blueprint accepted. (quickstart US4)

### Tests for User Story 4 ⚠️ (write first, must fail)

- [ ] T050 [P] [US4] Negative test: blueprint whose content ≠ sealed `ContentHash` rejected and not stored, in `tests/Sorcha.Blueprint.Service.Tests/BlueprintRecoveryProvenanceTests.cs`
- [ ] T051 [P] [US4] Negative test: blueprint with no sealed digest not stored; honest-path: matching digest accepted, in `tests/Sorcha.Blueprint.Service.Tests/BlueprintRecoveryHonestPathTests.cs`

### Implementation for User Story 4

- [ ] T052 [P] [US4] Add `ContentHash` to `PublishedBlueprintEntry`, in `src/Common/Sorcha.ServiceClients/.../IRegisterServiceClient.cs`
- [ ] T053 [US4] Compute SHA-256 over canonical blueprint JSON at publish time (sealed via `control.blueprint.publish`), with a shared canonical-JSON serializer in `src/Services/Sorcha.Blueprint.Service/`
- [ ] T054 [US4] Verify the recomputed canonical hash against the sealed `ContentHash` before storing; reject on mismatch/missing, in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/BlueprintRecoveryService.cs`
- [ ] T055 [US4] Increment `sorcha_blueprint_recovery_rejected_total{reason}`, in `BlueprintRecoveryService.cs`

**Checkpoint**: US1–US4 independently functional.

---

## Phase 7: User Story 5 - Presentation replay hardening (Priority: P2)

**Goal**: KB-JWT carries a mandatory, independently-checked short expiry; revocation re-checked at verify time.

**Independent Test**: Expired KB-JWT replay rejected within an open session; missing-exp rejected; mid-session revocation fails verification. (quickstart US5)

### Tests for User Story 5 ⚠️ (write first, must fail)

- [ ] T056 [P] [US5] Negative test: KB-JWT replayed after its `exp` rejected within an open session, in `tests/Sorcha.Verifier.Engine.Tests/KbJwtExpiryTests.cs`
- [ ] T057 [P] [US5] Negative test: KB-JWT with no `exp` rejected; over-long-lived KB-JWT (> `KbJwtMaxLifetimeSeconds`) rejected, in `tests/Sorcha.Verifier.Engine.Tests/KbJwtMissingExpTests.cs`
- [ ] T058 [P] [US5] Negative test: device credential revoked mid-session fails verification (revocation re-checked at verify time), in `tests/Sorcha.Verifier.Engine.Tests/MidSessionRevocationTests.cs`

### Implementation for User Story 5

- [ ] T059 [US5] Require and validate KB-JWT `exp` against wall-clock within clock skew (T003), before delegation/status validation; reject missing `exp`, in `src/Common/Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs`
- [ ] T060 [US5] Enforce `KbJwtMaxLifetimeSeconds` upper bound on `exp − iat`, in `src/Common/Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs`
- [ ] T061 [US5] Increment `sorcha_presentation_replay_rejected_total{reason}`, in `VerifiablePresentationValidator.cs`

**Checkpoint**: US1–US5 independently functional.

---

## Phase 8: User Story 6 - Open-participant key binding (Priority: P3)

**Goal**: For open/unpublished participants, the carried delivery key must be bound to a verifiable prior artifact; published participants unaffected.

**Independent Test**: Unbound carried key into an open slot rejected; key bound to a valid invitation accepted; published-participant path unchanged. (quickstart US6)

### Tests for User Story 6 ⚠️ (write first, must fail)

- [ ] T062 [P] [US6] Negative test: unbound carried key for open/unpublished participant rejected; commitment-mismatch rejected, in `tests/Sorcha.Blueprint.Service.Tests/CarriedKeyBindingTests.cs`
- [ ] T063 [P] [US6] Test: carried key bound to a valid unconsumed invitation accepted; published-participant ("published wins") path unaffected, in `tests/Sorcha.Blueprint.Service.Tests/CarriedKeyHonestPathTests.cs`

### Implementation for User Story 6

- [ ] T064 [US6] Add an invitation commitment lookup (commitment derived from `RegisterInvitationRecord.Nonce` + carried key), in `src/Services/Sorcha.Tenant.Service/Services/RegisterInvitationService.cs`
- [ ] T065 [US6] For open + unpublished participants, require the carried key to match the binding artifact; reject unbound/mismatch; leave the published-wins path untouched, in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
- [ ] T066 [US6] Increment `sorcha_carried_key_rejected_total{reason}`, in `ActionExecutionService.cs`

**Checkpoint**: All six stories independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, security hardening verification, and whole-feature validation

- [ ] T067 Verify the demo-mint / JWK-registry issuer resolver is **structurally excluded** from production composition (not flag-gated), with a test asserting it, in `tests/Sorcha.Verifier.Engine.Tests/ProductionIssuerCompositionTests.cs`
- [ ] T068 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` with the Feature 138 trust-hardening surfaces (status-list verification, peer identity, sealed-roster authority, control.validator.* actions, blueprint content hash, KB-JWT expiry, carried-key binding)
- [ ] T069 [P] Update `docs/reference/API-DOCUMENTATION.md` and affected service READMEs (Peer, Validator, Blueprint, Wallet, Tenant) for changed contracts/config
- [ ] T070 [P] Add a CLAUDE.md Critical Patterns entry for the federation fail-closed/sealed-state trust rule (one concise section)
- [ ] T071 Run `dotnet test` and confirm >85% coverage on new code (constitution IV); confirm every quickstart negative variant has a passing test (FR-021 / SC-008)
- [ ] T072 Execute `specs/138-federation-trust-hardening/quickstart.md` end-to-end against the Docker stack and record results

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: depends on Setup; blocks US1/US2/US5 (clock skew T003) and US1/US2 (health T004). US3/US4/US6 do not depend on T003/T004.
- **User Stories (Phases 3–8)**: depend on Foundational; otherwise mutually independent and parallelizable
- **Polish (Phase 9)**: depends on the targeted stories being complete

### User Story Dependencies

- **US1 (P1)**: needs T003 (clock skew). Independent of other stories. **MVP.**
- **US2 (P1)**: needs T003, T004. Independent.
- **US3 (P1)**: independent (no T003/T004 dependency). Establishes `PERM-*` primitives.
- **US4 (P2)**: independent.
- **US5 (P2)**: needs T003. Mid-session-revocation behavior is *strengthened* by US1's fail-closed change but the exp checks are independently testable.
- **US6 (P3)**: independent.

### Within Each Story

- Tests first (must fail) → models/proto → services → wiring/metrics
- Same-file tasks are sequential; different-file tasks marked [P] parallelize

### Parallel Opportunities

- Setup T001/T002 parallel; Foundational T004 parallel with T003-dependent prep
- Once Phase 2 done, all six stories can proceed in parallel by different developers
- All `[P]` test tasks within a story run together; proto/model `[P]` tasks within a story run together

---

## Parallel Example: User Story 1

```bash
# Tests first (all parallel — distinct files):
Task: "Forged-signature rejected — tests/Sorcha.Verifier.Engine.Tests/StatusListSignatureTests.cs"
Task: "iss mismatch rejected — tests/Sorcha.Verifier.Engine.Tests/StatusListIssuerPinningTests.cs"
Task: "Fail-closed on fetch failure — tests/Sorcha.Verifier.Engine.Tests/StatusListFailClosedTests.cs"
Task: "Expired list rejected + honest path — tests/Sorcha.Verifier.Engine.Tests/StatusListFreshnessTests.cs"

# Then implementation — T014 (publisher kid) parallel with the StatusListCache work;
# T009–T013 are sequential (same file: StatusListCache.cs / VerifiablePresentationValidator.cs).
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (T003 at minimum)
2. Phase 3 US1 (status-list signature verification + fail-closed)
3. **STOP and VALIDATE**: run quickstart US1; revocation forgery closed with the smallest blast radius
4. Ship

### Incremental Delivery

US1 → US2 → US3 (P1 wave: the structural gaps "someone will try first") → US4 → US5 (P2) → US6 (P3). Each story ships and validates independently.

### Parallel Team Strategy

After Phase 2: Dev A → US1+US5 (verifier engine), Dev B → US2 (peer), Dev C → US3 (validator), Dev D → US4+US6 (blueprint). US3 is the largest; weight staffing there.

---

## Notes

- [P] = different files, no incomplete-task dependency
- Every hardened surface has a negative test proving the forged/unsigned/replayed variant is rejected (FR-021)
- All new trust decisions fail **closed**; verify tests assert rejection, not silent fallback
- Commit after each task or logical group; each story is a clean PR boundary (branch + PR policy)
- EF migrations (T026): set `$env:ConnectionStrings__Sorcha__Postgres`, avoid `--no-build`
