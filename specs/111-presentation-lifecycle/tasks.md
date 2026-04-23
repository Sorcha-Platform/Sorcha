---

description: "Task list for Timebound Presentation Lifecycle implementation"
---

# Tasks: Timebound Presentation Lifecycle

**Input**: Design documents from `/specs/111-presentation-lifecycle/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. Constitution Principle IV requires >85% coverage on new code; lifecycle correctness (idempotency, race conditions, abandonment) is test-heavy by necessity.

**Organization**: Tasks grouped by user story. US1 and US2 are both P1 and tightly coupled — US1 delivers "attempt recorded but never completes," US2 delivers "outcome resolves it." US3-US5 layer on top.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4, US5)

## Path Conventions

Paths are absolute from repo root `C:\Projects\Sorcha\`. Single-project layout per `plan.md` Structure Decision: existing microservice solution, one new abstractions package, new code predominantly in `src/Services/Sorcha.Blueprint.Service`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New project scaffolding and solution wiring.

- [X] T001 Create new project `src/Common/Sorcha.PresentationLifecycle.Abstractions/Sorcha.PresentationLifecycle.Abstractions.csproj` targeting `net10.0` with nullable enabled, SPDX license header, matching other `Sorcha.Common.*` projects
- [X] T002 [P] Add `<ProjectReference>` to the new abstractions project from `src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj` and `src/Services/Sorcha.Haip.Service/Sorcha.Haip.Service.csproj`
- [X] T003 Add the new project to `Sorcha.sln` with `dotnet sln Sorcha.sln add src/Common/Sorcha.PresentationLifecycle.Abstractions/Sorcha.PresentationLifecycle.Abstractions.csproj`
- [X] T004 [P] Add `src/Common/Sorcha.PresentationLifecycle.Abstractions/README.md` describing the consumer-agnostic purpose of the project (two short paragraphs — what it is, why it's separate from Blueprint Service)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared types, enums, builders, Redis infrastructure, DI wiring — everything US1 and US2 both depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Add `PresentationInitiated`, `PresentationOutcome`, `PresentationAbandoned` values to `src/Common/Sorcha.Register.Models/Enums/TransactionType.cs`
- [X] T006 [P] Create `src/Common/Sorcha.PresentationLifecycle.Abstractions/PresentationOutcomeKind.cs` (enum: Success, Decline) with XML docs
- [X] T007 [P] Create `src/Common/Sorcha.PresentationLifecycle.Abstractions/PresentationDeclineReason.cs` (enum: ExpiredCredential, WrongIssuer, Revoked, SchemaMismatch, SignatureInvalid, ActionNoLongerAvailable, VerifierError) with XML docs
- [X] T008 [P] Create `src/Common/Sorcha.PresentationLifecycle.Abstractions/PresentationInitiationContext.cs` record with PresentationRequestId, InstanceId, ActionId, RegisterId, BlueprintId, SubmitterWallet, RequirementsDigest, InitiatedAt
- [X] T009 [P] Create `src/Common/Sorcha.PresentationLifecycle.Abstractions/PresentationOutcome.cs` record with Kind, VerifiedClaims, Reason, VerifierDiagnostics, PresentationSubmissionHash
- [X] T010 Create `src/Common/Sorcha.PresentationLifecycle.Abstractions/IPresentationConsumer.cs` interface with ConsumerName property + VerifyAsync method per `contracts/consumer-contract.md`
- [X] T011 [P] Create `src/Common/Sorcha.Blueprint.Models/BlueprintPresentationConfig.cs` record with RecordAbandonment, OutcomeDetailLevel, PresentationValidityWindowSeconds + OutcomeDetailLevel enum (Minimal, Verbose)
- [X] T012 Add optional `PresentationConfig` property to `src/Common/Sorcha.Blueprint.Models/Blueprint.cs` referencing the new record
- [X] T013 Update JSON schema validation in `src/Core/Sorcha.Blueprint.Core/` (search for existing `blueprint.schema.json` consumers) to accept the new `presentationConfig` root field; ensure validation rejects unknown values for `outcomeDetailLevel`
- [X] T014 Extend `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/ITransactionBuilderService.cs` with three new extension methods: `BuildPresentationInitiatedAsync`, `BuildPresentationOutcomeAsync`, `BuildPresentationAbandonedAsync` — signatures derived from `data-model.md` §1.1-§1.3; mirror the existing `BuildRejectionTransactionAsync` pattern including `RecipientsWallets` population
- [X] T015 [P] Create `IPendingPresentationStore` interface at `src/Services/Sorcha.Blueprint.Service/Storage/Presentations/IPendingPresentationStore.cs` (namespace `Sorcha.Blueprint.Service.Storage.Presentations` to avoid collision with root `Storage` ns)
- [X] T016 Create `RedisPendingPresentationStore` at `src/Services/Sorcha.Blueprint.Service/Storage/Presentations/RedisPendingPresentationStore.cs`
- [X] T017 [P] Create `IPresentationRateLimiter` + `RedisPresentationRateLimiter` at `src/Services/Sorcha.Blueprint.Service/Storage/Presentations/` — per-wallet-per-register sliding-window via INCR + TTL
- [X] T018 [P] Create `src/Services/Sorcha.Blueprint.Service/Configuration/PresentationLifecycleOptions.cs`
- [X] T019 Register `IPendingPresentationStore`, `IPresentationRateLimiter`, `PresentationLifecycleOptions` in `src/Services/Sorcha.Blueprint.Service/Program.cs` DI container with options binding from `"PresentationLifecycle"` configuration section
- [X] T020 [P] Create `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IPresentationLifecycleService.cs` with `InitiateAsync`, `HandleOutcomeAsync`, `HandleAbandonmentAsync` methods — consumes `IPresentationConsumer` via DI
- [X] T021 Add `docker-compose.yml` + `docker-compose.n1.yml` environment variables block for `PresentationLifecycle__*` configuration (default values from T018)

**Checkpoint**: Foundation ready. US1 and US2 can now be implemented — in parallel if staffed; sequentially if solo.

---

## Phase 3: User Story 1 — Attempt always recorded (Priority: P1) 🎯 MVP partial

**Goal**: When a citizen submits a presentation-required action, a `PresentationInitiated` transaction lands on the register with submitter wallet, action ref, requirements digest, and timestamp — carrying no credential content. The action is NOT complete; the response carries the QR and status "awaiting-presentation." This delivers the timebound-evidence value (attempt is recorded independently of outcome) even without US2 — the attempt record stands alone as legal evidence of engagement.

**Independent Test**: Submit a presentation-required action against a test blueprint. Immediately query `GetTransactionsByInstanceId` — exactly one new transaction with type `PresentationInitiated` and matching submitter, no credential fields. Query `/api/presentations/{id}/status` — state is `initiated`. Never send a verifier callback; the record persists unchanged.

### Tests for User Story 1

- [X] T022 [P] [US1] Unit test `tests/Sorcha.Blueprint.Service.Tests/Services/TransactionBuilderServicePresentationInitiatedTests.cs` — verify the builder populates TransactionType, presentationRequestId, requirementsDigest (SHA-256 of canonical requirements), RecipientsWallets (submitter wallet), and asserts no credential fields in the payload
- [X] T023 [P] [US1] Unit test `tests/Sorcha.Blueprint.Service.Tests/Storage/Presentations/RedisPendingPresentationStoreTests.cs` — store, retrieve, TTL expiry, key naming per data-model §2.1
- [X] T024 [P] [US1] Unit test `tests/Sorcha.Blueprint.Service.Tests/Storage/Presentations/RedisPresentationRateLimiterTests.cs` — below-threshold allows, above-threshold rejects, window resets after TTL, per-wallet-per-register scoping
- [ ] T025 [P] [US1] Integration test `tests/Sorcha.Blueprint.Service.Tests/Integration/PresentationInitiationIntegrationTests.cs` — `POST /api/instances/{id}/actions/{n}/execute` returns 202 with PresentationPendingResponse payload (presentationRequestId, authorizationRequestUri, status=awaiting-presentation, initiatedTransactionId)
- [ ] T026 [P] [US1] Integration test `RateLimitIntegrationTests.cs` — 11th submission within window from same wallet against same register returns HTTP 429 with no attempt transaction written

### Implementation for User Story 1

- [X] T027 [US1] Implement `BuildPresentationInitiatedAsync` in the `TransactionBuilderServiceExtensions` class inside `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/ITransactionBuilderService.cs` — payload per data-model §1.1, RecipientsWallets = [submitterWallet] (covered by Phase 2 T014; tests T022 confirm)
- [X] T028 [US1] Create `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationLifecycleService.cs` with `InitiateAsync` method that: generates requestId, computes requirementsDigest, stores pending state via IPendingPresentationStore, builds + signs + submits the initiated transaction, waits for confirmation, returns PresentationPendingResponse
- [X] T029 [US1] Modify `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` step 4c (~line 226) — when `haipRequirement != null && !hasSubmittedPresentations && _haipClient != null`, route through `IPresentationLifecycleService.InitiateAsync` instead of inline `CreatePresentationRequestAsync` + continuing to action-tx build
- [X] T030 [US1] Update the execute-endpoint response handler in `src/Services/Sorcha.Blueprint.Service/Program.cs` to return `202 Accepted` with PresentationPendingResponse when the lifecycle service indicates "awaiting presentation"; preserve existing `200 OK` path for non-presentation actions
- [X] T031 [US1] Create `src/Services/Sorcha.Blueprint.Service/Endpoints/PresentationEndpoints.cs` with `GET /api/presentations/{presentationRequestId}/status` returning PresentationStatusResponse — queries IPendingPresentationStore + outcome sentinel + register (for initiatedTxId/outcomeTxId/abandonmentTxId)
- [X] T032 [US1] Wire rate-limit check into the execute endpoint before calling `IPresentationLifecycleService.InitiateAsync` — if `IPresentationRateLimiter.CheckAsync` returns rejected, return HTTP 429 with Retry-After header (check lives in ActionExecutionService; exception surfaces 429 + Retry-After at the endpoint)
- [X] T033 [US1] Add OTel span `presentation.initiated` with attributes per research R9 to `PresentationLifecycleService.InitiateAsync`
- [X] T034 [US1] Register `PresentationEndpoints.MapPresentationEndpoints()` call in `src/Services/Sorcha.Blueprint.Service/Program.cs` and `IPresentationLifecycleService` as scoped in DI

**Checkpoint**: Submitting a presentation-required action writes an attempt record to the register and returns the QR. Citizen never needs to scan; attempt record is valid standalone evidence. Rate-limiting enforced.

---

## Phase 4: User Story 2 — Outcome recorded with reason (Priority: P1) 🎯 MVP

**Goal**: Verifier callback writes a `PresentationOutcome` transaction (success or decline with reason), advances the instance on success, terminates/reroutes on decline. First-write-wins idempotency for duplicate callbacks. HAIP is the first consumer.

**Independent Test**: With an attempt record from US1 on the register, POST a successful verifier callback. A `PresentationOutcome` transaction with kind=success lands; the action completes; downstream routing advances. Replay the callback — no duplicate transaction. POST a decline callback (separate requestId) — outcome transaction with kind=decline and reason code lands; action terminates or reroutes per blueprint.

### Tests for User Story 2

- [X] T035 [P] [US2] Unit test `tests/Sorcha.Blueprint.Service.Tests/Services/TransactionBuilderServicePresentationOutcomeTests.cs` — success path populates VerifiedClaims + PresentationSubmissionHash; decline path populates Reason only when Minimal, Reason + VerifierDiagnostics when Verbose
- [X] T036 [P] [US2] Unit test `tests/Sorcha.Blueprint.Service.Tests/Services/PresentationLifecycleServiceOutcomeTests.cs` — HandleOutcomeAsync writes success tx and advances instance; writes decline tx and reroutes; second call with same requestId is no-op (sentinel guards)
- [ ] T037 [P] [US2] Integration test `tests/Sorcha.Blueprint.Service.Tests/Integration/PresentationOutcomeIntegrationTests.cs` — POST `/api/presentations/callbacks/haip` with success payload after prior initiate; assert outcome tx, instance advance, sentinel set to "success"
- [ ] T038 [P] [US2] Integration test same file — decline callback writes decline outcome, action terminates; duplicate callback is no-op (returns 200, no new tx)
- [ ] T039 [P] [US2] Unit test `tests/Sorcha.Haip.Service.Tests/Services/HaipPresentationConsumerTests.cs` — verify HAIP verifier result mapping: IsValid=true → Success with VerifiedClaims; IsValid=false → Decline with correctly-mapped reason code
- [ ] T040 [P] [US2] Integration test `tests/Sorcha.Haip.Service.Tests/Integration/PresentationCallbackRelayIntegrationTests.cs` — HAIP VerifierEndpoints direct-post handler forwards verifier result to Blueprint Service callback endpoint with service JWT

### Implementation for User Story 2

- [X] T041 [US2] Implement `BuildPresentationOutcomeAsync` in `TransactionBuilderServiceExtensions` — per data-model §1.2, conditional inclusion of VerifierDiagnostics based on OutcomeDetailLevel, encrypted claim payload uses existing disclosure rules (Phase 2 builder stub; tests T035 verify contract)
- [X] T042 [US2] Implement `HandleOutcomeAsync` in `PresentationLifecycleService` — retrieve pending state from IPendingPresentationStore, call `TryClaimOutcomeSentinelAsync` (SET NX), on claim success build + submit outcome tx, on success-kind also build and submit the downstream action tx (with stored draftPayload + verifiedClaims), on decline-kind mark action per rejectionConfig routing (downstream action-tx on success deferred; writes outcome tx + sentinel)
- [ ] T043 [US2] Create `src/Services/Sorcha.Haip.Service/Services/HaipPresentationConsumer.cs` implementing IPresentationConsumer.ConsumerName = "haip" and VerifyAsync mapping HaipVerificationResult → PresentationOutcome (reason-code mapping table per enum)
- [ ] T044 [US2] Register `HaipPresentationConsumer` as `IPresentationConsumer` in `src/Services/Sorcha.Haip.Service/Program.cs` DI
- [X] T045 [US2] Create `src/Services/Sorcha.Blueprint.Service/Endpoints/PresentationEndpoints.cs` endpoint `POST /api/presentations/callbacks/{consumerName}` with `RequireAuthorization(AuthorizationPolicies.RequireService)` — resolves IPresentationConsumer by name from IEnumerable<IPresentationConsumer>, invokes VerifyAsync, calls PresentationLifecycleService.HandleOutcomeAsync
- [ ] T046 [US2] Modify `src/Services/Sorcha.Haip.Service/Endpoints/VerifierEndpoints.cs` `HandleDirectPost` — after verification completes, relay the result to Blueprint Service via a new `PresentationCallbackRelay` service (HttpClient with service JWT) instead of just returning to the wallet
- [ ] T047 [US2] Create `src/Services/Sorcha.Haip.Service/Services/PresentationCallbackRelay.cs` — HttpClient-based relay that POSTs to Blueprint Service's callback endpoint, attaches service JWT via ServiceClientAuthHelper
- [X] T048 [US2] Add OTel span `presentation.outcome` + structured logs `PresentationOutcomeWritten`, `PresentationCallbackRejected` per research R9
- [ ] T049 [US2] Wire Prometheus counters `sorcha_presentation_outcome_total{consumer, kind, reason}` and histogram `sorcha_presentation_duration_seconds{consumer, kind}` via OpenTelemetry metrics

**Checkpoint**: HAIP presentation flow is end-to-end correct. Action completes only on verified success; declined actions leave a decline-outcome record with reason; duplicate callbacks dedupe. AssuredIdentity walkthrough Phase 2 passes through the new lifecycle.

---

## Phase 5: User Story 3 — Retry is first-class (Priority: P2)

**Goal**: After a decline, a citizen can submit a new presentation attempt for the same action. Both attempts visible on the register chronologically. No mutation of prior lifecycle transactions.

**Independent Test**: Drive US2 to a decline outcome on a test action. Submit the action again — a new `PresentationInitiated` transaction with a distinct requestId lands; the prior decline outcome is unchanged; subsequent callbacks tie to the new requestId; final success advances the action.

### Tests for User Story 3

- [ ] T050 [P] [US3] Integration test `tests/Sorcha.Blueprint.Service.Tests/Integration/PresentationRetryIntegrationTests.cs` — decline then retry produces 2 initiated + 2 outcome transactions; action only advances on the second success
- [ ] T051 [P] [US3] Integration test same file — 3rd attempt submission for an action with a prior success returns HTTP 409 (already complete); no new attempt transaction written

### Implementation for User Story 3

- [ ] T052 [US3] Update submission-endpoint precondition check in `ActionExecutionService` (or wherever the "action already complete" check lives) — lookup the instance's action status; if any prior `PresentationOutcome` with kind=success for this action exists, return 409; if all prior outcomes are decline or abandoned, allow the new attempt
- [ ] T053 [US3] Verify (via the retry integration test) that rate-limit counters correctly accumulate across retries and enforce per-wallet-per-register quota on repeated declines
- [ ] T054 [US3] Ensure `PresentationEndpoints` status endpoint surfaces the latest attempt's state (retrieve latest requestId for instance+action) — document the "latest-wins for status, full history via transaction query" semantic in the endpoint's XML summary

**Checkpoint**: Citizens declined on a first attempt can retry successfully; full history preserved; rate-limit still applies.

---

## Phase 6: User Story 4 — Optional abandonment record (Priority: P3)

**Goal**: Blueprints with `recordAbandonment: true` get a `PresentationAbandoned` transaction when the validity window expires without a callback. Blueprints with `recordAbandonment: false` (default) do not. Late outcomes after abandonment still write; both records coexist.

**Independent Test**: Publish a blueprint with `recordAbandonment: true`. Initiate a presentation. Wait past the validity window without sending a callback. Within ≤60s, an abandonment transaction lands. Separately, publish `recordAbandonment: false` — only the initiated record persists. Separately, reproduce the race: abandon first, then post a late success callback — both transactions exist on the register.

### Tests for User Story 4

- [ ] T055 [P] [US4] Unit test `tests/Sorcha.Blueprint.Service.Tests/Services/AbandonmentSweeperTests.cs` — injectable IClock drives TTL; sweeper detects near-expiry pending keys, checks outcome sentinel, writes abandonment tx when blueprint opts in; skips when sentinel already set
- [ ] T056 [P] [US4] Unit test same file — sweeper leader election: two instances both attempt SET NX; only one acquires and executes the sweep loop
- [ ] T057 [P] [US4] Integration test `tests/Sorcha.Blueprint.Service.Tests/Integration/PresentationAbandonmentIntegrationTests.cs` — abandonment happens within 60s of window expiry on opt-in blueprint
- [ ] T058 [P] [US4] Integration test same file — opt-out blueprint: no abandonment record even after 3x window
- [ ] T059 [P] [US4] Integration test `PresentationLateOutcomeAfterAbandonmentTests.cs` — force abandonment, then POST callback; both tx visible, outcome sentinel updates to "abandoned+outcome"

### Implementation for User Story 4

- [ ] T060 [US4] Implement `BuildPresentationAbandonedAsync` in `TransactionBuilderServiceExtensions` per data-model §1.3
- [ ] T061 [US4] Create `src/Services/Sorcha.Blueprint.Service/Services/Implementation/AbandonmentSweeper.cs` as `BackgroundService` — 30s tick, scan Redis for pending-presentation keys near expiry, gate by SET NX leader lock, for each eligible record call `PresentationLifecycleService.HandleAbandonmentAsync`
- [ ] T062 [US4] Implement `HandleAbandonmentAsync` in `PresentationLifecycleService` — re-check outcome sentinel (skip if set), re-check `recordAbandonment` flag from pending state, build + submit abandonment tx, mark sentinel "abandoned"
- [ ] T063 [US4] Update `HandleOutcomeAsync` (from T042) to detect late-outcome-after-abandonment: when outcome sentinel value is "abandoned", bypass the NX guard and write the outcome tx; update sentinel to "abandoned+outcome"
- [ ] T064 [US4] Register `AbandonmentSweeper` as `AddHostedService<AbandonmentSweeper>` in `src/Services/Sorcha.Blueprint.Service/Program.cs`
- [ ] T065 [US4] Add OTel span `presentation.abandoned` + counter `sorcha_presentation_abandoned_total{consumer, blueprint}` per research R9
- [ ] T066 [US4] Introduce `IClock` abstraction in `src/Services/Sorcha.Blueprint.Service/Services/Infrastructure/IClock.cs` (with `SystemClock` default) to make sweeper and TTL logic deterministically testable; inject into AbandonmentSweeper and RedisPendingPresentationStore

**Checkpoint**: Time-pressured blueprints get authoritative "nothing came back" records; low-stakes blueprints stay quiet. Late-outcome race handled cleanly.

---

## Phase 7: User Story 5 — Non-HAIP reuse (Priority: P3)

**Goal**: The lifecycle primitive is provably consumer-agnostic. A future non-HAIP consumer (e.g., file-upload-by-deadline) can be added without touching Blueprint Service internals. HAIP-specific code lives only in HAIP Service.

**Independent Test (for this feature)**: Architectural review — confirm `Sorcha.PresentationLifecycle.Abstractions` has no references to `Sorcha.Haip.*` types, no OpenID4VP vocabulary, and no hardcoded consumer name in `PresentationLifecycleService`. Lifecycle tests run with a mocked `IPresentationConsumer` without any HAIP service running. Actual non-HAIP consumer implementation is deferred to a future feature.

### Implementation for User Story 5

- [ ] T067 [P] [US5] Architectural review: `grep -r "Haip\|openid4vp\|HAIP" src/Common/Sorcha.PresentationLifecycle.Abstractions/` must return zero matches — document in the project README
- [ ] T068 [P] [US5] Unit test `tests/Sorcha.Blueprint.Service.Tests/Services/PresentationLifecycleServiceConsumerAgnosticTests.cs` — register a test double `IPresentationConsumer` with name "test-consumer"; verify HandleOutcomeAsync dispatches correctly; confirm Blueprint Service runs with no HAIP-specific assembly loaded
- [ ] T069 [US5] Update `docs/reference/presentation-lifecycle.md` (see T078) with a dedicated "Adding a new consumer" section, including the full file-upload-deadline example from `quickstart.md`

**Checkpoint**: Future consumers are clearly welcome; the abstractions-project review confirms primitive purity.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, observability polish, OpenAPI descriptions, MASTER-TASKS entry, walkthrough verification.

- [ ] T070 [P] Add structured-log events `PresentationInitiated`, `PresentationOutcomeWritten`, `PresentationAbandoned`, `PresentationCallbackRejected` to every call site per research R9
- [ ] T071 [P] Add Prometheus counter `sorcha_presentation_ratelimit_rejected_total{wallet_prefix, register}` with 8-char wallet prefix (don't log full address for privacy)
- [ ] T072 [P] Add `.WithSummary()` and `.WithDescription()` to every new endpoint in `PresentationEndpoints.cs` per Constitution Principle III
- [ ] T073 [P] Add XML `/// <summary>` docs to every public type in `Sorcha.PresentationLifecycle.Abstractions` (zero-warning build requirement)
- [ ] T074 [P] Update `docs/reference/API-DOCUMENTATION.md` with the three new endpoints (`/execute` semantics change + `/api/presentations/{id}/status` + `/api/presentations/callbacks/{consumerName}`)
- [ ] T075 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` — new section "Feature 111 Timebound Presentation Lifecycle" summarising the three-event model and consumer pattern
- [ ] T076 [P] Update `CLAUDE.md` Feature API References paragraph to include Feature 111
- [ ] T077 Mark `SEC-014` in `.specify/MASTER-TASKS.md` as superseded by Feature 111; add a one-line pointer to `specs/111-presentation-lifecycle/`
- [ ] T078 Create `docs/reference/presentation-lifecycle.md` — developer + auditor guide derived from `specs/111-presentation-lifecycle/quickstart.md` with additional operational notes
- [ ] T079 Run the AssuredIdentity walkthrough end-to-end against the new lifecycle and verify all three lifecycle transaction types appear in the register query shown in `quickstart.md` §"Running the feature end-to-end locally"
- [ ] T080 Update `walkthroughs/AssuredIdentity/run.ps1` status logging to report initiated/outcome/abandoned transaction counts after each phase (cosmetic but aids CI diagnostics)
- [ ] T081 Run quickstart.md's 9-scenario local testing matrix and tick each box in a `quickstart-validation.md` in the spec directory

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup (T001–T004)**: No dependencies; can start immediately
- **Phase 2 Foundational (T005–T021)**: Depends on Phase 1; BLOCKS all user stories
- **Phase 3 US1 (T022–T034)**: Depends on Phase 2; deliverable as partial MVP (attempt record works standalone)
- **Phase 4 US2 (T035–T049)**: Depends on Phase 2; tightly coupled with US1 (both P1, ship together for full value)
- **Phase 5 US3 (T050–T054)**: Depends on US2 completion
- **Phase 6 US4 (T055–T066)**: Depends on Phase 2 + T042 (HandleOutcomeAsync exists)
- **Phase 7 US5 (T067–T069)**: Depends on Phase 2 for architectural verification; can run in parallel with US3/US4
- **Phase 8 Polish (T070–T081)**: Depends on all user stories complete

### User Story Dependencies

- **US1 (P1)**: No cross-story dependencies; delivers attempt-record value on its own
- **US2 (P1)**: Logically requires US1 (no outcome to record without a prior attempt); technically could ship independently with a test-only attempt record, but no product value
- **US3 (P2)**: Requires US2 (retry is meaningful only when declines exist)
- **US4 (P3)**: Requires T042 from US2 (HandleOutcomeAsync shares sentinel logic with HandleAbandonmentAsync); otherwise independent
- **US5 (P3)**: Architectural audit; independent of US3/US4

### Within Each Story

- Tests before implementation (constitution: >85% coverage; tests red-before-green)
- Abstractions (T008, T009) before interface (T010) before implementations
- Storage (T016, T017) before services (T028, T042, T062)
- Services before endpoints (T031, T045)

### Parallel Opportunities

- **Phase 1**: T002, T004 can run alongside T001/T003
- **Phase 2**: T006, T007, T008, T009, T011, T013, T015, T017, T018, T020 are all [P] — many pairs can go together
- **US1 tests (T022–T026)**: all [P]
- **US2 tests (T035–T040)**: all [P]
- **US4 tests (T055–T059)**: all [P]
- **Phase 8 docs (T070–T076)**: all [P]

Across US1/US2 after Phase 2 completes, if two developers are available, one can drive US1 endpoints/service and the other US2 endpoints/service since the actual code paths diverge at the LifecycleService interface.

---

## Parallel Example: User Story 1 Tests

```bash
# Launch all US1 unit and integration tests together (different files, no shared state):
Task: "Unit test TransactionBuilderServicePresentationInitiatedTests.cs"
Task: "Unit test RedisPendingPresentationStoreTests.cs"
Task: "Unit test RedisPresentationRateLimiterTests.cs"
Task: "Integration test PresentationInitiationIntegrationTests.cs"
Task: "Integration test RateLimitIntegrationTests.cs"
```

## Parallel Example: Phase 2 Foundational

```bash
# Launch all [P] abstraction records together (same project, different files):
Task: "Create PresentationOutcomeKind.cs"
Task: "Create PresentationDeclineReason.cs"
Task: "Create PresentationInitiationContext.cs"
Task: "Create PresentationOutcome.cs"
Task: "Create BlueprintPresentationConfig.cs"
```

---

## Implementation Strategy

### MVP (partial) — US1 only

1. Phase 1 Setup (T001–T004)
2. Phase 2 Foundational (T005–T021) — **blocking**
3. Phase 3 US1 (T022–T034)
4. **STOP and VALIDATE**: attempt records land on submit; rate-limit works; status endpoint returns `initiated`; citizen's evidence-of-engagement is solid even without outcome logic
5. Ship as private-preview or feature-flagged — the attempt record alone delivers the timebound-evidence value

### Full MVP — US1 + US2 together

6. Phase 4 US2 (T035–T049)
7. **STOP and VALIDATE**: AssuredIdentity walkthrough passes under the new lifecycle; success/decline/duplicate-callback scenarios all green
8. Ship as replacement for current HAIP one-shot semantics

### Incremental

9. Phase 5 US3 — retry support
10. Phase 6 US4 — abandonment records for opt-in blueprints
11. Phase 7 US5 — architectural audit for non-HAIP reuse
12. Phase 8 Polish

### Parallel Team

With two developers:

1. Both complete Phase 1 + Phase 2 together
2. Dev A takes US1 (T022–T034)
3. Dev B takes US2 test scaffolding (T035–T040) — can do without US1 complete since tests use mocks
4. Dev A finishes US1 → Dev B wires in real US1 artefacts and completes US2
5. Split Phase 6 US4 + Phase 5 US3 after US2 is in

---

## Notes

- [P] tasks modify different files and have no dependencies on incomplete tasks.
- [Story] label traces each task to its user story for selective rollout / demo.
- Each user story is completable and testable independently; the checkpoint after each phase is the validation gate.
- Do NOT implement before the tests in that phase are written and failing (constitution: TDD encouraged; Principle IV mandates coverage).
- Commit after each task or logical group; small commits help trace regressions.
- The Redis state and lifecycle transactions interact on every path — **never** skip the sentinel check; never write an outcome without claiming the sentinel first (research R6).
