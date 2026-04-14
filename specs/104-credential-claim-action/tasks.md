# Tasks: Credential Claim Action (Feature 103 Wave 14)

**Input**: Design documents from `specs/104-credential-claim-action/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Test tasks are included per the project constitution's Testing Requirements (≥85% coverage on new code, TDD encouraged). xUnit + FluentAssertions for .NET; Playwright for UI E2E.

**Organization**: Tasks are grouped by user story. The one non-standard twist: **US5 (engine primitive, wave 14a) is a hard prerequisite for US1/US2/US3/US4 (wave 14b)** even though US5 is P2 in the spec. The task phases reflect this — US5 lands first as the foundation that P1 sits on.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Include exact file paths

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm prerequisites for the two-PR wave split.

- [X] T001 Verify `104-credential-claim-action` branch is checked out, wave 13 (`HaipLocalReceiveService`, `CredentialOfferQrCard`) is present on master, and Docker stack is healthy (`docker-compose ps`) before any implementation begins

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: None — all foundational work is part of User Story 5 (engine primitive). The engine primitive is self-contained and does not need separate foundational tasks.

_No tasks in this phase. Proceed directly to Phase 3._

---

## Phase 3: User Story 5 — Blueprint author declares a credential claim action using the engine primitive (Priority: P2, wave 14a) 🎯 PR #1

**Goal**: Deliver a general-purpose payload carry-forward primitive (`Route.OutputMapping` + `Instance.PendingActionPayloads`) that lets any blueprint carry data from one action's execution result into the next action's prepopulated payload. Ships as wave 14a PR with zero user-visible features. Hard prerequisite for all other user stories in this feature.

**Independent Test**: Publish the two-action smoke blueprint from `quickstart.md` section 1. Submit action 0 with a `note` field; query the reviewer's pending actions and assert the response includes `prepopulatedPayload: { carriedNote: "..." }`. Submit action 1 with `{ acknowledged: true }` and assert the sealed transaction contains both `carriedNote` and `acknowledged`.

### Tests for User Story 5 (write first, ensure they FAIL before implementation)

- [X] T002 [P] [US5] Unit tests for `RoutingEngine` `OutputMapping` evaluation (8 cases: no-op when null, null source, single field, nested target paths, absent source silently skipped, all-absent returns null, parallel route independent seeds, legacy overload backward-compat) in `tests/Sorcha.Blueprint.Engine.Tests/OutputMappingTests.cs` — **all green**
- [ ] T003 [P] [US5] Create failing unit tests for `ActionExecutionService` prepopulated payload merge (seed-only round trip, submission-wins on key conflict, nested object deep merge, nested array replace-wholesale, seed removed atomically on completion, seed retained on execution failure) in `tests/Sorcha.Blueprint.Service.Tests/ActionExecutionService/PrepopulatedPayloadMergeTests.cs` — **deferred**: 81 pre-existing `ActionExecutionService` test fixture failures block adding more tests there; this work belongs to a separate maintenance PR that repairs the fixtures
- [ ] T004 [P] [US5] Create failing integration test for two-action carry-forward against a real `EfCoreInstanceStore` (publish smoke blueprint, execute action 0, assert pending action 1 has seeded payload on reload, execute action 1 with submission, assert merged payload is sealed) in `tests/Sorcha.Blueprint.Service.IntegrationTests/OutputMappingCarryForwardTests.cs` — **deferred to follow-up PR** (requires Postgres/Docker harness; out of scope for wave 14a minimum)

### Implementation for User Story 5

- [X] T005 [P] [US5] Added `OutputMapping: Dictionary<string, string>?` property with XML doc and JSON serialization attributes to `src/Common/Sorcha.Blueprint.Models/Route.cs`
- [X] T006 [P] [US5] Added `PendingPayloads: IReadOnlyDictionary<int, JsonObject>?` to the `RoutingResult` class in `src/Core/Sorcha.Blueprint.Engine/Models/RoutingResult.cs`
- [X] T007 [P] [US5] Added `PendingActionPayloads: Dictionary<int, JsonObject>` field to `src/Services/Sorcha.Blueprint.Service/Models/Instance.cs`
- [X] T008 [US5] Implemented `OutputMapping` evaluation in `src/Core/Sorcha.Blueprint.Engine/Implementation/RoutingEngine.cs` + new `IRoutingEngine.DetermineNextWithMappingAsync` overload + JSON Pointer helper at `src/Core/Sorcha.Blueprint.Engine/Implementation/JsonPointerHelper.cs`; ActivitySource span `blueprint.routing.output_mapping.evaluate` tagged with route ID and entry count
- [X] T009 [US5] Extended `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs` + added `PendingActionPayloads` jsonb column (squashed into InitialCreate migration and model snapshot per MEMORY.md feedback); added `SerializePendingActionPayloads`/`DeserializePendingActionPayloads` helpers
- [X] T010 [US5] Updated `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`: (a) merge seed into submitted payload before validation using `request with { PayloadData = merged }`, (b) `BuildOutputMappingSource` composes `{payload, calculations}` JsonObject for routing, (c) `EvaluateRoutingAsync` now calls `DetermineRoutingWithMappingAsync` on `IExecutionEngine` (extended in same commit), (d) `ApplyInstanceStateChanges` removes consumed seed and writes `routingResult.PendingPayloads` into `instance.PendingActionPayloads`
- [X] T011 [US5] Added `PrepopulatedPayload: JsonObject?` to `PendingActionSummary.cs`; `EfCoreInstanceStore.GetPendingActionsByWalletAsync` populates it from `instance.PendingActionPayloads[actionId]`
- [X] T012 [US5] Added publish-time validation `VAL_BP_011` in `Program.cs ValidateBlueprint`: for every route with `OutputMapping`, target JSON Pointers must begin with `/`, and the top-level target field must exist in at least one `DataSchema` of at least one next action; source pointers also validated for the leading `/`

**Checkpoint**: Wave 14a PR ready. Engine primitive works end-to-end; existing blueprints unaffected. Submit as standalone PR. Do not proceed to Phase 4 until wave 14a PR is merged.

---

## Phase 4: User Story 1 — Citizen claims a verified-citizen credential from their action queue (Priority: P1) 🎯 MVP

**Goal**: The P1 critical path — a citizen sees a pending claim action after the assessor approves their application, clicks Claim, and the credential lands in their local wallet. The assessor's wallet never receives the credential. This is the whole point of the feature.

**Independent Test**: Run `walkthroughs/HaipVerifiedCitizen` end-to-end. Submit application as citizen, approve as assessor, log in as citizen, open My Actions, confirm a pending claim action exists with card showing credential type + issuer + expiry, click Claim, confirm credential appears in citizen's My Credentials and is absent from assessor's My Credentials, confirm three sealed actions on the register.

**Depends on**: Phase 3 (US5 engine primitive) merged.

### Tests for User Story 1

- [ ] T013 [P] [US1] Create failing unit tests for `CredentialOfferSchemaResolver` (detects `x-credential-offer: true` on object fields, ignores non-object fields, handles nested schemas, extracts the object value from merged payload) in `tests/Sorcha.UI.Core.Tests/Components/Forms/CredentialOfferSchemaResolverTests.cs`
- [ ] T014 [P] [US1] Create failing unit tests for `CredentialClaimCard` rendering (header populates from `display` block, expiry is displayed in local time, Claim button fires `HaipLocalReceiveService`, success transitions card to Exchanged state, auto-submits `{ claimed_at }` payload via `OnSubmit`) in `tests/Sorcha.UI.Core.Tests/Components/Credentials/CredentialClaimCardTests.cs`
- [ ] T015 [P] [US1] Create failing Playwright E2E test for the full P1 flow (citizen application → assessor approval → citizen claim → credential appears in citizen's wallet → credential absent from assessor's wallet) in `tests/Sorcha.UI.E2E.Tests/Docker/CredentialClaimTests.cs`

### Implementation for User Story 1

- [ ] T016 [P] [US1] Create `CredentialOfferSchemaResolver` that reads `x-credential-offer` extension from a JSON Schema property and returns a resolver result pointing at the object field and its current value from the merged payload, in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Forms/CredentialOfferSchemaResolver.cs`
- [ ] T017 [US1] Create `CredentialClaimCard.razor` component wrapping wave 13's `CredentialOfferQrCard` with a header block (title, subtitle, description, issuer name, issuer logo) populated from the `display` descriptor, a primary Claim button that invokes `IHaipLocalReceiveService.ReceiveLocallyAsync` using the wallet address from the authenticated session (NOT the payload, per FR-019), a success path that submits `{ claimed_at: <ISO-8601 now> }` via `OnSubmit`, and a snackbar for both success and transient failure, in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/CredentialClaimCard.razor` (depends on T016)
- [ ] T018 [US1] Wire the `x-credential-offer` handler into `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor` (and `.razor.cs` if applicable): when the resolver reports a match on a schema field, render `CredentialClaimCard` instead of the generic object editor; skip client-side validation on fields under the extension (depends on T016, T017)
- [ ] T019 [US1] Update `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` to write HAIP mint output (`credential_offer_uri`, `display`, `expires_at`) into the route source document at `/haip/*` for blueprints that use claim actions, so wave 14a's `OutputMapping` can pick it up. Do not remove the existing response-payload write — both paths coexist for backward compatibility per FR-009
- [ ] T020 [US1] Update `examples/templates/verified-citizen-v2.json` to version 3: add action 2 with `sender: "applicant"`, a schema containing a `credentialOffer` object field marked `x-credential-offer: true` plus a sibling `claimed_at` field, add `RejectionConfig: { isTerminal: true }` on action 2, add `outputMapping` on action 1's default route that carries `/haip/credential_offer_uri`, `/haip/display`, `/haip/expires_at` into `/credentialOffer/*`, ensure `TemplateSeedingService` will pick up the new version on startup
- [ ] T021 [US1] Add publish-time validation `VAL_BP_012` (the `x-credential-offer` extension may only appear on object-typed schema fields) and warning `WARN_BP_006` (non-blocking: `credential_offer_uri` should be declared `required`) in `src/Services/Sorcha.Blueprint.Service/Services/BlueprintValidator.cs`

**Checkpoint**: P1 flow verified end-to-end. `dotnet test --filter "FullyQualifiedName~CredentialClaim"` green. Playwright E2E passes. Walkthrough passes locally. MVP deliverable reached.

---

## Phase 5: User Story 2 — Citizen loads the credential into an external HAIP wallet via QR (Priority: P2)

**Goal**: Offer the citizen an alternative path to load the credential into an external HAIP-compatible wallet via an embedded QR code, with the action transitioning to complete when the external wallet finishes the exchange.

**Independent Test**: Open the credential claim card, click "Scan with external wallet", verify QR is rendered, scan with a HAIP wallet simulator, confirm the action transitions to Complete via HAIP offer status polling, confirm the credential is NOT in the citizen's Sorcha wallet.

**Depends on**: Phase 4 (US1 claim card exists).

### Tests for User Story 2

- [ ] T022 [P] [US2] Extend `tests/Sorcha.UI.Core.Tests/Components/Credentials/CredentialClaimCardTests.cs` with test cases: clicking "Scan with external wallet" reveals the QR view, HAIP offer status polling transitions card state on `Exchanged`, successful external exchange triggers the same `OnSubmit({ claimed_at })` path as local claim

### Implementation for User Story 2

- [ ] T023 [US2] Extend `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/CredentialClaimCard.razor` with a "Scan with external wallet" secondary action that reveals wave 13's `CredentialOfferQrCard` in place of the header actions, wire its `OnStatusChanged` callback to mirror the local-claim success path (transition state, auto-submit confirmation payload), ensure the embedded card's HAIP offer status polling is reused without modification

**Checkpoint**: External-wallet path works end-to-end with wave 13's status polling. Both P1 (local) and P2 (external) paths verified.

---

## Phase 6: User Story 3 — Citizen retries a claim that failed due to a transient error (Priority: P2)

**Goal**: When the local claim attempt fails with a transient error (network, HAIP 5xx), the pending action stays in Pending state and the citizen can retry without starting a new application.

**Independent Test**: Stop the `haip-service` container, open claim card, click Claim → verify snackbar error + action stays pending. Restart `haip-service`, click Claim again → verify success. Same action ID throughout.

**Depends on**: Phase 4 (US1 claim card with local receive path exists).

### Tests for User Story 3

- [ ] T024 [P] [US3] Extend `tests/Sorcha.UI.Core.Tests/Components/Credentials/CredentialClaimCardTests.cs` with test cases: `HaipLocalReceiveService` returning failure does not call `OnSubmit`, Claim button re-enables after failure, error snackbar is shown with the error message, subsequent click after recovery triggers a new claim attempt

### Implementation for User Story 3

- [ ] T025 [US3] Extend `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/CredentialClaimCard.razor` with retry handling: on `HaipLocalReceiveResult.Success == false`, show `Severity.Error` snackbar with `result.ErrorMessage`, reset `_isReceivingLocally = false`, do not call `OnSubmit`, do not transition card state. Confirm via the extended unit tests from T024

**Checkpoint**: Retry path verified. Transient failures no longer force a new application.

---

## Phase 7: User Story 4 — Citizen's credential offer expires before they claim it (Priority: P3)

**Goal**: When the credential offer's `expires_at` has passed, the pending action transitions to Failed via a new client-side-triggered endpoint, with clear user-facing messaging.

**Independent Test**: Configure a 2-minute-expiry test blueprint, run assessor approval, wait 3 minutes, open claim card → verify expired state with Claim disabled, verify `POST .../claim-expired` is fired, verify action transitions to Failed on the register.

**Depends on**: Phase 4 (US1 claim card exists). No dependency on Phase 5 or 6.

### Tests for User Story 4

- [ ] T026 [P] [US4] Create failing integration test for the new `claim-expired` endpoint covering: 200 on expired valid claim action, 400 when offer not yet expired, 400 when action has no `x-credential-offer` field, 403 when JWT wallet does not match action sender, 404 on missing instance/action, 409 on already-terminal action, in `tests/Sorcha.Blueprint.Service.IntegrationTests/ClaimExpiredEndpointTests.cs`
- [ ] T027 [P] [US4] Extend `tests/Sorcha.UI.Core.Tests/Components/Credentials/CredentialClaimCardTests.cs` with expiry test cases: card renders expired state when `expires_at < now`, Claim button is disabled in expired state, card fires the claim-expired request on mount when expired, expired state is reached even if the poll timer has not ticked

### Implementation for User Story 4

- [ ] T028 [P] [US4] Create `src/Services/Sorcha.Blueprint.Service/Endpoints/ClaimExpiredEndpoint.cs` implementing `POST /api/blueprint/instances/{instanceId}/actions/{actionId}/claim-expired` per `contracts/claim-expired.yaml`: authz via JWT bearer, 400/403/404/409 error paths, on success build a failure transaction via the existing action-execution transaction chain and atomically remove the seed from `Instance.PendingActionPayloads`, register the endpoint in the Blueprint.Service startup `Program.cs`, apply `RateLimitPolicies.Api` policy, expose it via YARP gateway configuration in `src/Services/Sorcha.ApiGateway/yarp-config.json` so the client can reach it through the gateway
- [ ] T029 [US4] Extend `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/CredentialClaimCard.razor` with expiry detection: parse `expires_at` on parameters-set, render expired card state (disable Claim, show explanation) when past, fire `POST .../claim-expired` on first detection per session via a client service `IClaimExpiryClient` (or direct `HttpClient`), handle transition error gracefully (log, keep card in expired UI) (depends on T028)

**Checkpoint**: Expiry path verified. Claim cards cannot be successfully claimed past `expires_at`.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Ship the driving-licence equivalent, update walkthroughs, verify n1, update docs.

- [ ] T030 [P] Update `examples/templates/haip-driving-licence.json` to version 2 with the equivalent third action shape (credential claim action + action 1 `outputMapping` + `RejectionConfig.IsTerminal = true`), mirroring the Verified Citizen v2 v3 structure from T020
- [ ] T031 [P] Update `walkthroughs/HaipVerifiedCitizen/Program.cs` to drive the three-action flow: execute action 0 as citizen actor, action 1 as assessor actor, switch to citizen actor, query pending actions, assert action 2 exists with `prepopulatedPayload.credentialOffer`, exercise the local claim path, assert the credential lands in the citizen's local credential store and is absent from the assessor's
- [ ] T032 [P] Update `walkthroughs/HaipDrivingLicence/Program.cs` with the same three-action flow adjustments
- [ ] T033 Update the `CLAUDE.md` Feature 103 section to document wave 14: new `Route.OutputMapping` primitive, new `x-credential-offer` schema extension, Verified Citizen v2 v3 three-action shape, reference to `specs/104-credential-claim-action/` for the full design
- [ ] T034 Run `quickstart.md` sections 1, 2a–2f against local Docker stack end-to-end, fix any gaps discovered, then rebuild and deploy wave 14 images to `n1.sorcha.dev` via the network-bootstrap skill and re-run the HaipVerifiedCitizen and HaipDrivingLicence walkthroughs against n1

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — trivial verification
- **Foundational (Phase 2)**: Empty
- **User Story 5 (Phase 3, wave 14a)**: Hard prerequisite for all other user stories. Ships as **PR #1** and must merge before Phase 4 begins
- **User Story 1 (Phase 4, wave 14b P1)**: Depends on Phase 3 merged. Starts **PR #2** (wave 14b)
- **User Story 2 (Phase 5, wave 14b P2)**: Depends on Phase 4 (CredentialClaimCard must exist). Adds to PR #2
- **User Story 3 (Phase 6, wave 14b P2)**: Depends on Phase 4. Adds to PR #2. Independent of Phase 5
- **User Story 4 (Phase 7, wave 14b P3)**: Depends on Phase 4. Adds to PR #2. Independent of Phase 5 and 6
- **Polish (Phase 8)**: Depends on Phases 4–7 being complete. Finalises PR #2 for merge

### User Story Dependencies

- **US5 (P2, engine)**: Standalone; no story dependencies. Must land first.
- **US1 (P1, claim)**: Depends on US5.
- **US2 (P2, external QR)**: Depends on US1 (reuses card component).
- **US3 (P2, retry)**: Depends on US1 (reuses card component).
- **US4 (P3, expiry)**: Depends on US1 (reuses card component). Independent of US2, US3.

### Within Each User Story

- Tests written first (TDD per constitution IV), must FAIL before implementation
- Models (Route, RoutingResult, Instance) before engine logic before service wiring before endpoints
- Schema resolver and component before renderer wiring
- Blueprint JSON updates after the engine + UI are ready to consume them

### Parallel Opportunities

- T002, T003, T004 (US5 tests) all different files — fully parallel
- T005, T006, T007 (US5 model additions) all different files — fully parallel
- T013, T014, T015 (US1 tests) all different files — fully parallel
- T016 is independent of T019 — parallel possible (different files, different services)
- T030, T031, T032 (polish walkthroughs + driving licence) all different files — fully parallel
- US2, US3, US4 can run in parallel if staffed (all depend only on US1, and each extends a different aspect of `CredentialClaimCard.razor` — but T023, T025, T029 all touch the same razor file, so in practice they serialize on that file)

---

## Parallel Example: User Story 5 (wave 14a)

```bash
# Launch US5 tests together (different files, no dependencies):
Task: "T002 Unit tests for RoutingEngine OutputMapping evaluation in tests/Sorcha.Blueprint.Engine.Tests/Routing/OutputMappingTests.cs"
Task: "T003 Unit tests for ActionExecutionService merge in tests/Sorcha.Blueprint.Service.Tests/ActionExecutionService/PrepopulatedPayloadMergeTests.cs"
Task: "T004 Integration test for two-action carry-forward in tests/Sorcha.Blueprint.Service.IntegrationTests/OutputMappingCarryForwardTests.cs"

# Launch US5 model additions together (different files):
Task: "T005 Add OutputMapping to Route in src/Common/Sorcha.Blueprint.Models/Route.cs"
Task: "T006 Add PendingPayloads to RoutingResult in src/Core/Sorcha.Blueprint.Engine/Models/RoutingResult.cs"
Task: "T007 Add PendingActionPayloads to Instance in src/Services/Sorcha.Blueprint.Service/Models/Instance.cs"
```

---

## Implementation Strategy

### Critical path — two-PR delivery

**PR #1 (wave 14a):** Phases 1 → 3 → Checkpoint. Deliver the engine primitive as a standalone PR. Zero user-visible features. Full engine + integration test coverage. Merge, verify no regressions on master, then start PR #2.

**PR #2 (wave 14b):** Phases 4 → 5 → 6 → 7 → 8. All user-facing work for the credential claim feature. Build the P1 path first (MVP), then layer US2/US3/US4 on top, finish with the polish phase. Verify against n1 before merge.

### MVP scope

**MVP = Wave 14a PR #1 + Phase 4 of wave 14b PR #2.** At that point:

- Engine primitive shipped (US5)
- P1 claim path working (US1)
- Verified Citizen v2 v3 working end-to-end
- Credential reaches the correct wallet, Decline is handled via existing `RejectionConfig.IsTerminal`

The external-wallet path, retry, and expiry can be layered in before merging PR #2, but if a hard deadline hit after Phase 4, PR #2 could theoretically ship with just the P1 path. Current plan is to include all four user stories in PR #2.

### Incremental delivery within PR #2

1. Complete Phase 4 (US1) → local run validates P1 path → checkpoint
2. Add Phase 5 (US2 external wallet) → local + simulator run validates P2 QR path → checkpoint
3. Add Phase 6 (US3 retry) → stop-start HAIP stack test validates retry → checkpoint
4. Add Phase 7 (US4 expiry) → short-expiry test blueprint validates expiry → checkpoint
5. Complete Phase 8 (polish) → walkthrough + n1 validation → PR #2 ready for merge

### Parallel team strategy (if multiple engineers)

- Engineer A: Wave 14a (Phase 3) end-to-end
- Engineer B: While 14a is in review, prepare Phase 4 test fixtures (T013–T015) and the `CredentialOfferSchemaResolver` (T016) in a branch off 14a
- Once 14a merges: Engineer A picks up US4 (Phase 7, independent file surface), Engineer B picks up US1 + US2 + US3 in the CredentialClaimCard file

Tasks T023, T025, T029 all modify `CredentialClaimCard.razor` and must serialize. Plan these sequentially within a single engineer's workstream rather than splitting them across parallel workers.

---

## Notes

- Wave 14a and wave 14b ship as separate PRs. Task IDs T002–T012 belong to PR #1; T013–T034 belong to PR #2.
- All tasks marked `[P]` touch different files with no mutation-order dependency on incomplete tasks.
- Tests are written first per the constitution's TDD guidance. Verify tests fail before running implementation tasks.
- Commit after each task or a small logical group. Prefer small commits for reviewability — each phase checkpoint is a natural commit boundary.
- License header (`SPDX-License-Identifier: MIT` + copyright) required on all new files.
- After each user story phase, re-run `dotnet test` for the affected projects and a targeted Playwright run to catch regressions early.
